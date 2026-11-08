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
        private readonly List<ReplayObject> _waiting = new();
        private readonly Dictionary<string, Shown> _shown = new Dictionary<string, Shown>();
        private readonly HashSet<string> _missLogged = new HashSet<string>();
        float _nextScan;
        int _scanLeft = 40;
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

            int removed = 0;
            int matched = 0;
            var used = new HashSet<int>();
            _waiting.Clear();
            foreach (var obj in _doc.Objects)
            {
                if (obj.RemovedAt < 0f) continue;
                string type = obj.Type ?? "";
                bool structure = type.IndexOf("Building", StringComparison.OrdinalIgnoreCase) >= 0
                    || type.IndexOf("Scenery", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!structure) continue;
                removed++;

                Unit match = null;
                string id = SanitizeId(obj.Id);
                if (id.Length > 0 && byName.TryGetValue(id, out var candidates) && candidates.Count > 0)
                    match = candidates[0];
                if (match == null && obj.Id != null && byName.TryGetValue(obj.Id, out var raw) && raw.Count > 0)
                    match = raw[0];

                if (match == null && obj.Samples.Count > 0)
                {
                    var s = obj.Samples[0];
                    Vector3 want = Plugin.GlobalToLocal(s.X, s.Y, s.Z);
                    float best = 40f;
                    foreach (var u in allUnits)
                    {
                        if (u == null || u.definition == null) continue;
                        if (!NameMatches(u, obj)) continue;
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
                    if (used.Add(match.GetInstanceID()))
                    {
                        _tracked.Add((obj, match));
                        matched++;
                    }
                }
                else
                {
                    _waiting.Add(obj);
                    NoteUnmatched(obj, allUnits);
                }
            }
            Plugin.Log?.LogInfo("ReplayStaticHandler: " + matched + " zugeordnet, warten " + _waiting.Count + " von " + removed);
        }

        static string SanitizeId(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var sb = new System.Text.StringBuilder(raw.Length);
            foreach (char c in raw)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
                else sb.Append('_');
            }
            return sb.ToString();
        }

        static bool NameMatches(Unit unit, ReplayObject obj)
        {
            string name = obj.Name ?? "";
            if (name.Length == 0 || unit.definition == null) return false;
            if (string.Equals(unit.definition.unitName, name, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(unit.definition.code, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        void NoteUnmatched(ReplayObject obj, Unit[] allUnits)
        {
            string name = obj.Name ?? "";
            string id = obj.Id ?? "";
            if (!_missLogged.Add(id + "|" + name)) return;
            if (_missLogged.Count > 12) return;
            string where = "no-sample";
            float nearest = -1f;
            string nearName = "";
            if (obj.Samples.Count > 0)
            {
                var s = obj.Samples[0];
                Vector3 want = Plugin.GlobalToLocal(s.X, s.Y, s.Z);
                where = want.ToString("0.#");
                foreach (var u in allUnits)
                {
                    if (u == null || u.definition == null) continue;
                    if (!NameMatches(u, obj)) continue;
                    float d = Vector3.Distance(u.transform.position, want);
                    if (nearest < 0f || d < nearest)
                    {
                        nearest = d;
                        nearName = u.definition.unitName + " @" + u.transform.position.ToString("0.#");
                    }
                }
            }
            Plugin.Log?.LogInfo("Wreck unmatched " + name + " id=" + id
                + " want=" + where
                + " nearest=" + (nearest < 0f ? "none" : nearest.ToString("0.#") + " " + nearName));
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
            if (_waiting.Count > 0 && _scanLeft > 0 && Time.unscaledTime >= _nextScan)
            {
                _scanLeft--;
                _nextScan = Time.unscaledTime + 0.5f;
                int before = _tracked.Count;
                BuildLookup();
                if (_tracked.Count != before)
                    Plugin.Log?.LogInfo("ReplayStaticHandler rescan " + _tracked.Count);
            }
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
                    ShowWreck(unit, shown, obj.RemovedAt);
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

        void ShowWreck(Unit unit, Shown shown, float born)
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
                string rn = r.GetType().Name;
                if (rn == "ParticleSystemRenderer" || rn == "TrailRenderer") continue;
                if (r.GetComponentInParent<DamageParticles>() != null) continue;
                r.enabled = false;
                hidden.Add(r);
            }
            shown.Hidden = hidden.ToArray();
            shown.Wreck = SpawnWreck(unit, born);
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

        static FieldInfo? WreckField(Type type)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (var t = type; t != null; t = t.BaseType)
            {
                var f = t.GetField("wreckage", flags);
                if (f != null) return f;
            }
            return null;
        }

        static GameObject? SpawnWreck(Unit unit, float born)
        {
            string label = unit.definition != null ? unit.definition.unitName : unit.name;
            var field = WreckField(unit.GetType());
            if (field?.GetValue(unit) is not GameObject prefab || prefab == null)
            {
                Plugin.Log?.LogInfo("Wreck no prefab " + label + " type=" + unit.GetType().Name);
                return null;
            }
            // Stay inactive until the instance is already at the building.
            // SetParent(null) on an active instance runs FragmentManager.OnEnable
            // immediately, and that unparents the pieces where they are.
            var hold = new GameObject("NOReplay_WreckHold");
            hold.SetActive(false);
            GameObject go;
            try { go = UnityEngine.Object.Instantiate(prefab, hold.transform); }
            catch
            {
                UnityEngine.Object.Destroy(hold);
                return null;
            }
            go.SetActive(false);
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
                if (n == "Wreckage")
                    mb.enabled = false;
            }
            go.transform.SetPositionAndRotation(unit.transform.position, unit.transform.rotation);
            go.transform.SetParent(null, true);
            go.SetActive(true);
            var fm = go.GetComponentInChildren<FragmentManager>(true);
            if (fm is Behaviour fmBeh) fmBeh.enabled = false;
            var motion = go.GetComponent<ReplayWreckMotion>();
            if (motion == null) motion = go.AddComponent<ReplayWreckMotion>();
            motion.Born = born;
            if (fm != null)
                motion.Arm(fm, unit.GetInstanceID());
            else
                Plugin.Log?.LogInfo("Wreck no FragmentManager " + label + " prefab=" + prefab.name);
            motion.PlayPieces();
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true))
            {
                if (rb == null) continue;
                rb.useGravity = false;
                rb.isKinematic = true;
                rb.detectCollisions = false;
            }
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
                if (c != null) c.enabled = false;
            UnityEngine.Object.Destroy(hold);
            float now = Plugin.Clock != null ? Plugin.Clock.Time : 0f;
            Plugin.Log?.LogInfo("Wreck " + label + " type=" + unit.GetType().Name + " prefab=" + prefab.name
                + " unit=" + unit.transform.position.ToString("0.#")
                + " wreck=" + go.transform.position.ToString("0.#")
                + " piece0=" + motion.FirstPiece.ToString("0.#")
                + " datum=" + Datum.originPosition.ToString("0.#")
                + " born=" + born.ToString("0.00")
                + " now=" + now.ToString("0.00")
                + " pieces=" + motion.PieceCount);
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