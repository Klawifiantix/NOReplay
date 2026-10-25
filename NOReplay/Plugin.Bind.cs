using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace NOReplay
{
    public partial class Plugin
    {
        static void TickBindGhosts()
        {
            if (_dedupAt > 0f && Time.unscaledTime >= _dedupAt)
            {
                _dedupAt = 0f;
                // DedupNearbyStatics disabled
            }
            if (!_bindGhosts) return;
            if (Time.unscaledTime < _bindAt) return;
            _bindGhosts = false;
            try { BindGhostsToMissionUnits(); }
            catch (Exception ex) { Log.LogError("Bind Ghosts: " + ex); }
            // DedupNearbyStatics disabled
            _dedupAt = Time.unscaledTime + 2f;
        }

        internal static void ScheduleBindGhosts()
        {
            if (CurrentDoc == null) return;
            _bindGhosts = true;
            _bindAt = Time.unscaledTime + 0.5f;
            Log.LogInfo("Binde Ghosts in 0,5s an Missions-Units.");
        }

        static void DedupNearbyStatics()
        {
            var list = new List<(GameObject go, string key, Vector3 p, bool ghost)>();
            var seen = new HashSet<int>();
            void add(GameObject go, bool ghost)
            {
                if (go == null) return;
                if (!go.scene.IsValid()) return;
                int id = go.GetInstanceID();
                if (!seen.Add(id)) return;
                string key = StaticKeyName(go);
                if (string.IsNullOrEmpty(key)) return;
                list.Add((go, key, go.transform.position, ghost));
            }
            for (int i = 0; i < _ghosts.Count; i++)
            {
                var g = _ghosts[i];
                if (g == null) continue;
                var rg = g.GetComponent<ReplayGhost>();
                if (rg?.Track == null) continue;
                if (!IsBuilding(rg.Track.Type, rg.Track.Name)
                    && !IsScenery(rg.Track.Type, rg.Track.Name)
                    && !IsEmplacement(rg.Track.Type, rg.Track.Name))
                    continue;
                add(g, true);
            }
            string[] types = { "Building", "Scenery", "Unit" };
            for (int t = 0; t < types.Length; t++)
            {
                var ty = FindGameType(types[t]);
                if (ty == null) continue;
                foreach (var o in Resources.FindObjectsOfTypeAll(ty))
                {
                    var c = o as Component;
                    if (c == null) continue;
                    if (types[t] == "Unit" && !LooksLikeStaticName(c.gameObject.name)) continue;
                    add(c.gameObject, c.GetComponent<ReplayGhost>() != null);
                }
            }
            int killed = 0;
            const float maxDistSq = 60f * 60f;
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                if (a.go == null) continue;
                for (int j = i + 1; j < list.Count; j++)
                {
                    var b = list[j];
                    if (b.go == null) continue;
                    if (ReferenceEquals(a.go, b.go)) continue;
                    if (!string.Equals(a.key, b.key, StringComparison.OrdinalIgnoreCase)) continue;
                    float dx = a.p.x - b.p.x;
                    float dz = a.p.z - b.p.z;
                    if (dx * dx + dz * dz > maxDistSq) continue;
                    GameObject keep = a.go, drop = b.go;
                    if (a.ghost && !b.ghost) { keep = a.go; drop = b.go; }
                    else if (!a.ghost && b.ghost) { keep = b.go; drop = a.go; }
                    if (drop == null || !drop.activeSelf) continue;
                    var rends = drop.GetComponentsInChildren<Renderer>(true);
                    for (int r = 0; r < rends.Length; r++)
                        if (rends[r] != null) rends[r].enabled = false;
                    drop.SetActive(false);
                    killed++;
                    if (killed <= 12)
                        Log.LogInfo("Dedup " + a.key + " drop=" + drop.name + " keep=" + keep.name
                                    + " d=" + Mathf.Sqrt(dx * dx + dz * dz).ToString("0.0"));
                }
            }
            Log.LogInfo("Statik-Dedup Kandidaten=" + list.Count + " ausgeblendet=" + killed);
        }

        static void BindGhostsToMissionUnits()
        {
            if (CurrentDoc == null) return;
            var names = new[] { "Aircraft", "GroundVehicle", "Ship", "Building", "Unit" };
            int n = 0;
            var seen = new HashSet<int>();
            foreach (var tn in names)
            {
                var t = FindGameType(tn);
                if (t == null || !typeof(UnityEngine.Object).IsAssignableFrom(t)) continue;
                foreach (var o in Resources.FindObjectsOfTypeAll(t))
                {
                    if (o == null) continue;
                    var go = (o as Component)?.gameObject;
                    if (go == null) continue;
                    int id = go.GetInstanceID();
                    if (!seen.Add(id)) continue;
                    if (go.GetComponent<ReplayGhost>() != null) continue;
                    var obj = MatchReplay(ReadUniqueName(o) ?? go.name);
                    if (obj == null) continue;
                    var rg = go.AddComponent<ReplayGhost>();
                    rg.Track = obj;
                    ApplyReplayIdentity(go, obj);
                    SilenceAiOnly(go);
                    AddGhost(go);
                    rg.SetGhostVisible(false);
                    n++;
                }
            }
            Log.LogInfo("Ghosts an Missions-Units: +" + n + " gesamt=" + _ghosts.Count);
        }

        static void TryAttachFromSpawn(object[]? args, object? result, bool silence)
        {
            if (CurrentDoc == null) return;
            string? uid = null;
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                    if (args[i] is string s && s.Length >= 2 && s.Length <= 12)
                        uid = s;
            }
            GameObject? go = null;
            if (result is GameObject g) go = g;
            else if (result is Component c) go = c.gameObject;
            else if (result != null)
            {
                var t = result.GetType();
                if (t.IsGenericType && t.Name.StartsWith("ValueTuple", StringComparison.Ordinal))
                {
                    var item1 = t.GetField("Item1")?.GetValue(result);
                    go = item1 as GameObject ?? (item1 as Component)?.gameObject;
                }
            }
            if (go == null && args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] is GameObject ag && ag.scene.IsValid()) { go = ag; break; }
                    if (args[i] is Component ac && ac.gameObject.scene.IsValid()) { go = ac.gameObject; break; }
                }
            }
            if (go == null) return;
            var obj = MatchReplay(uid ?? go.name);
            if (obj == null) return;
            // No Ghost for objects that never moved – mission loader places them
            if (!ObjectActuallyMoved(obj))
                return;
            if (go.GetComponent<ReplayGhost>() != null) return;
            var rg = go.AddComponent<ReplayGhost>();
            rg.Track = obj;
            ApplyReplayIdentity(go, obj);
            SilenceAiOnly(go);
            AddGhost(go);
            rg.SetGhostVisible(false);
            if (_ghosts.Count <= 8 || (_ghosts.Count % 50) == 0)
                Log.LogInfo("Ghost " + (uid ?? "?") + " -> " + go.name + " n=" + _ghosts.Count);
            // First ghost becomes the follow target
            if (_ghost == null)
                _ghost = go;
        }
    }
}
