using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NOReplay
{
    /// <summary>
    /// Static buildings and scenery are not ghosts. At their removal time the
    /// building's own wreck prefab is shown, with physics and damage turned off.
    /// Scrubbing back before that time restores the intact building.
    /// </summary>
    internal class ReplayStaticHandler : MonoBehaviour
    {
        private ReplayDocument _doc;
        private readonly List<(ReplayObject obj, Unit unit)> _tracked = new();
        private readonly Dictionary<string, Shown> _shown = new Dictionary<string, Shown>();
        static bool _fxPatched;

        sealed class Shown
        {
            public bool Dead;
            public GameObject? Wreck;
            public Component[] Particles = Array.Empty<Component>();
            public Renderer[] Hidden = Array.Empty<Renderer>();
        }

        public static void StartFor(ReplayDocument doc)
        {
            if (doc == null) return;
            var go = new GameObject("NOReplay_StaticHandler");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            var h = go.AddComponent<ReplayStaticHandler>();
            h.Init(doc);
            // Auto-start progress display
            // ReplayLoadProgress.Begin("Lade Statik…");
        }

        public void Init(ReplayDocument doc)
        {
            _doc = doc;
            BuildLookup();
            Plugin.Log?.LogInfo($"ReplayStaticHandler ready – tracking {_tracked.Count} non-moving objects.");
        }

        private void BuildLookup()
        {
            _tracked.Clear();
            var allUnits = UnityEngine.Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
            var byName = new Dictionary<string, List<Unit>>();
            foreach (var u in allUnits)
            {
                if (u == null) continue;
                string key = u.UniqueName ?? u.name ?? "";
                if (string.IsNullOrEmpty(key)) continue;
                if (!byName.TryGetValue(key, out var list))
                {
                    list = new List<Unit>();
                    byName[key] = list;
                }
                list.Add(u);
            }

            int matched = 0;
            foreach (var obj in _doc.Objects)
            {
                if (obj.RemovedAt < 0f) continue;
                if (ObjectMoved(obj)) continue; // moving objects keep their Ghost

                Unit match = null;
                string id = obj.Id ?? "";
                if (byName.TryGetValue(id, out var candidates) && candidates.Count > 0)
                    match = candidates[0];

                if (match == null && obj.Samples.Count > 0)
                {
                    var s = obj.Samples[0];
                    Vector3 want = new Vector3(s.X, s.Y, s.Z);
                    float best = 12f;
                    foreach (var u in allUnits)
                    {
                        if (u == null || u.definition == null) continue;
                        if (u.definition.unitName != obj.Name && u.definition.code != obj.Name) continue;
                        float d = Vector3.Distance(u.transform.position, want);
                        if (d < best)
                        {
                            best = d;
                            match = u;
                        }
                    }
                }
                if (match != null)
                {
                    _tracked.Add((obj, match));
                    matched++;
                }
            }
            Plugin.Log?.LogInfo($"ReplayStaticHandler: {matched} zugeordnet");
        }

        private bool ObjectMoved(ReplayObject obj)
        {
            if (obj.Samples.Count < 2) return false;
            var a = obj.Samples[0];
            var b = obj.Samples[obj.Samples.Count - 1];
            float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return (dx * dx + dy * dy + dz * dz) > 4f;
        }

        void Update()
        {
            if (Plugin.CurrentDoc == null)
            {
                ClearVisuals();
                UnityEngine.Object.Destroy(gameObject);
                return;
            }
            if (_doc == null || Plugin.Clock == null) return;
            EnsureBuildingPatch();
            float t = Plugin.Clock.Time;
            bool paused = Plugin.Clock.Paused;
            for (int i = 0; i < _tracked.Count; i++)
            {
                var (obj, unit) = _tracked[i];
                if (unit == null) continue;
                string key = obj.Id ?? unit.GetInstanceID().ToString();
                if (!_shown.TryGetValue(key, out var shown))
                {
                    shown = new Shown();
                    _shown[key] = shown;
                }
                bool dead = obj.RemovedAt >= 0f && t >= obj.RemovedAt;
                if (dead && !shown.Dead)
                    ShowWreck(unit, shown);
                else if (!dead && shown.Dead)
                    Restore(unit, shown);
                if (!shown.Dead || shown.Particles.Length == 0) continue;
                if (paused)
                    ReplayGhost.PauseParticleSystems(shown.Particles);
                else
                {
                    for (int p = 0; p < shown.Particles.Length; p++)
                        ReplayGhost.PlayParticleSystem(shown.Particles[p]);
                }
            }
        }

        static void EnsureBuildingPatch()
        {
            if (_fxPatched) return;
            _fxPatched = true;
            var method = typeof(Building).GetMethod("UnitDisabled",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null) return;
            try
            {
                new Harmony("noreplay.buildingwreck").Patch(method,
                    prefix: new HarmonyMethod(typeof(ReplayStaticHandler), nameof(SkipBuildingFx)));
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("Building-Wreck: " + ex.Message);
            }
        }

        // During a replay the building's own UnitDisabled collapses the mesh and
        // can destroy it. Visuals are owned here so a rewind can put it back.
        static bool SkipBuildingFx()
        {
            return Plugin.CurrentDoc == null;
        }

        void ShowWreck(Unit unit, Shown shown)
        {
            shown.Dead = true;
            try
            {
                if (!unit.disabled)
                    unit.Networkdisabled = true;
            }
            catch { }
            var rends = unit.GetComponentsInChildren<Renderer>(true);
            var hidden = new List<Renderer>();
            for (int i = 0; i < rends.Length; i++)
            {
                var r = rends[i];
                if (r == null || !r.enabled) continue;
                r.enabled = false;
                hidden.Add(r);
            }
            shown.Hidden = hidden.ToArray();
            shown.Wreck = SpawnWreck(unit);
            shown.Particles = ParticlesOf(shown.Wreck);
        }

        void Restore(Unit unit, Shown shown)
        {
            shown.Dead = false;
            if (shown.Wreck != null)
            {
                UnityEngine.Object.Destroy(shown.Wreck);
                shown.Wreck = null;
            }
            shown.Particles = Array.Empty<Component>();
            for (int i = 0; i < shown.Hidden.Length; i++)
            {
                var r = shown.Hidden[i];
                if (r != null) r.enabled = true;
            }
            shown.Hidden = Array.Empty<Renderer>();
            try
            {
                if (unit.disabled)
                    unit.Networkdisabled = false;
            }
            catch { }
        }

        static GameObject? SpawnWreck(Unit unit)
        {
            var field = unit.GetType().GetField("wreckage",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field?.GetValue(unit) is not GameObject prefab || prefab == null) return null;
            var hold = new GameObject("NOReplay_WreckHold");
            hold.SetActive(false);
            GameObject go;
            try { go = UnityEngine.Object.Instantiate(prefab, hold.transform); }
            catch
            {
                UnityEngine.Object.Destroy(hold);
                return null;
            }
            go.name = "NOReplay_BuildingWreck";
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                string n = mb.GetType().Name;
                if (n == "DamageParticles")
                {
                    try { mb.GetType().GetField("fireDamage", flags)?.SetValue(mb, 0f); } catch { }
                    try { mb.GetType().GetField("stopWhenParentCulled", flags)?.SetValue(mb, false); } catch { }
                    continue;
                }
                if (n == "FragmentManager" || n == "Wreckage")
                    mb.enabled = false;
            }
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true))
            {
                if (rb == null) continue;
                rb.useGravity = false;
                rb.isKinematic = false;
                rb.constraints = RigidbodyConstraints.FreezeAll;
                rb.detectCollisions = false;
            }
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
                if (c != null) c.enabled = false;
            go.SetActive(true);
            go.transform.SetParent(null, true);
            go.transform.SetPositionAndRotation(unit.transform.position, unit.transform.rotation);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true))
                if (rb != null) rb.isKinematic = true;
            UnityEngine.Object.Destroy(hold);
            return go;
        }

        static Component[] ParticlesOf(GameObject? go)
        {
            if (go == null) return Array.Empty<Component>();
            var pt = Type.GetType("UnityEngine.ParticleSystem, UnityEngine.ParticleSystemModule")
                     ?? Plugin.FindGameType("ParticleSystem");
            if (pt == null) return Array.Empty<Component>();
            var raw = go.GetComponentsInChildren(pt, true);
            var list = new List<Component>(raw.Length);
            for (int i = 0; i < raw.Length; i++)
                if (raw[i] is Component c) list.Add(c);
            return list.ToArray();
        }

        void ClearVisuals()
        {
            foreach (var shown in _shown.Values)
            {
                if (shown.Wreck != null)
                    UnityEngine.Object.Destroy(shown.Wreck);
            }
            _shown.Clear();
        }

        void OnDestroy()
        {
            ClearVisuals();
            _tracked.Clear();
        }
    }
}