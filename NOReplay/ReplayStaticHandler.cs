using System;
using System.Collections.Generic;
using UnityEngine;

namespace NOReplay
{
    /// <summary>
    /// Handles destruction of non-moving objects (buildings, scenery, and units that never moved)
    /// by setting Networkdisabled. No ReplayGhost is created for these.
    /// </summary>
    internal class ReplayStaticHandler : MonoBehaviour
    {
        private ReplayDocument _doc;
        private readonly List<(ReplayObject obj, Unit unit)> _tracked = new();
        private readonly HashSet<string> _done = new();
        private float _nextCheck;

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
            if (_doc == null || Plugin.Clock == null) return;
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 0.25f;

            float t = Plugin.Clock.Time;
            int remaining = 0;
            for (int i = 0; i < _tracked.Count; i++)
            {
                var (obj, unit) = _tracked[i];
                if (unit == null) continue;
                string key = obj.Id ?? unit.GetInstanceID().ToString();
                if (_done.Contains(key)) continue;
                if (obj.RemovedAt > t)
                {
                    remaining++;
                    continue;
                }
                try
                {
                    if (!unit.disabled)
                        unit.Networkdisabled = true;
                    _done.Add(key);
                }
                catch
                {
                    if (unit != null) unit.gameObject.SetActive(false);
                    _done.Add(key);
                }
            }
        }

        void OnDestroy()
        {
            _tracked.Clear();
        }
    }
}