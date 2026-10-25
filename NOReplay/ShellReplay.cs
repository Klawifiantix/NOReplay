using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    static class ShellReplay
    {
        static ReplayObject[] _shells = Array.Empty<ReplayObject>();
        static ReplayDocument? _doc;
        static readonly List<GameObject> _pool = new List<GameObject>();
        static readonly HashSet<string> _boomed = new HashSet<string>();
        static readonly Dictionary<int, string> _burst = new Dictionary<int, string>();
        static float _lastTime;
        static bool _shown;
        static bool _missLogged;

        static int _shellGen = 1;
        static readonly int[] _slotShell = new int[36];
        static readonly int[] _slotShellGen = new int[36];

        public static void Tick(ReplayDocument? doc, float time)
        {
            if (doc == null) return;
            if (!ReferenceEquals(doc, _doc))
            {
                _doc = doc;
                _shells = Collect(doc);
                _boomed.Clear();
                _burst.Clear();
                Plugin.Log.LogInfo("Shells in ACMI: " + _shells.Length);
            }
            BurstReplay.Tick(doc, time);
            if (time + 0.05f < _lastTime)
            {
                _boomed.Clear();
                _burst.Clear();
                _shellGen++;
            }
            _lastTime = time;
            int slot = 0;
            Vector3 listen = ListenAt();
            bool haveListen = listen.sqrMagnitude > 100f;
            if (!haveListen && Camera.main != null)
            {
                listen = Camera.main.transform.position;
                haveListen = true;
            }
            for (int i = 0; i < _shells.Length && slot < 36; i++)
            {
                var o = _shells[i];
                if (o.SpawnAt > time) continue;
                if (o.RemovedAt >= 0f && time > o.RemovedAt + 1.2f) continue;
                if (o.Samples.Count < 2) continue;
                var a = o.Samples[0];
                int i1 = 1;
                while (i1 < o.Samples.Count - 1 && o.Samples[i1].Time - a.Time < 0.05f) i1++;
                var p1 = o.Samples[i1];
                var origin = Plugin.GlobalToLocal(a.X, a.Y, a.Z);
                var hit = Plugin.GlobalToLocal(p1.X, p1.Y, p1.Z);
                var dir = hit - origin;
                if (dir.sqrMagnitude < 4f) continue;
                float span = Mathf.Max(0.2f, p1.Time - a.Time);
                float speed = Mathf.Clamp(dir.magnitude / span, 180f, 900f);
                float age = time - a.Time;
                if (age < 0f || age > span + 0.15f) continue;
                int bucket = (int)(a.Time * 8f);
                int key = ((int)a.X / 20) * 73856093 ^ ((int)a.Z / 20) * 19349663 ^ bucket;
                if (_burst.TryGetValue(key, out var owner) && owner != o.Id) continue;
                _burst[key] = o.Id;
                var pos = origin + dir.normalized * Mathf.Min(speed * age, dir.magnitude);
                if (haveListen && (pos - listen).sqrMagnitude > 2000f * 2000f) continue;
                var go = Take(slot);
                var trail = go.GetComponent<TrailRenderer>();
                if (trail != null && (_slotShellGen[slot] != _shellGen || _slotShell[slot] != i))
                {
                    trail.emitting = false;
                    trail.time = 0f;
                    trail.Clear();
                    go.SetActive(false);
                    go.transform.position = pos;
                    go.SetActive(true);
                    trail.Clear();
                    trail.time = 0.16f;
                    trail.emitting = true;
                    _slotShellGen[slot] = _shellGen;
                    _slotShell[slot] = i;
                }
                slot++;
                go.transform.position = pos;
                go.SetActive(true);
                if (!_shown)
                {
                    _shown = true;
                    Plugin.Log.LogInfo("Shell fliegt " + o.Id + " @ " + pos + " v=" + speed.ToString("0"));
                }
                if (_boomed.Add(o.Id))
                    PulseNear(a);
            }
            if (!_shown && time > 7f && !_missLogged)
            {
                _missLogged = true;
                Plugin.Log.LogInfo("Shell unsichtbar t=" + time.ToString("0.0") + " listen=" + haveListen + " " + listen);
            }
            for (int i = slot; i < _pool.Count; i++)
            {
                var dead = _pool[i];
                if (dead != null && dead.activeSelf)
                {
                    var trail = dead.GetComponent<TrailRenderer>();
                    if (trail != null)
                    {
                        trail.emitting = false;
                        trail.time = 0f;
                        trail.Clear();
                    }
                    dead.SetActive(false);
                }
                if (i < _slotShellGen.Length) _slotShellGen[i] = 0;
            }
        }

        static Vector3 ListenAt()
        {
            if (Plugin.ListenPoint(out var pos) && pos.sqrMagnitude > 500f * 500f)
                return pos;
            var ghosts = Plugin.GhostComps;
            for (int i = 0; i < ghosts.Count; i++)
            {
                var rg = ghosts[i];
                if (rg == null || !rg.gameObject.activeInHierarchy) continue;
                if (Plugin.IsFollowed(rg.gameObject)) return rg.transform.position;
            }
            return Vector3.zero;
        }

        static bool NearGhost(Vector3 pos)
        {
            var ghosts = Plugin.GhostComps;
            if (ghosts.Count == 0) return true;
            float lim = 1800f * 1800f;
            for (int i = 0; i < ghosts.Count; i++)
            {
                var rg = ghosts[i];
                if (rg == null || !rg.gameObject.activeInHierarchy) continue;
                if ((rg.transform.position - pos).sqrMagnitude < lim) return true;
            }
            return false;
        }

        static void PulseNear(ReplaySample s)
        {
            var pos = Plugin.GlobalToLocal(s.X, s.Y, s.Z);
            var ghosts = Plugin.GhostComps;
            ReplayGhost? best = null;
            float bestD = 700f * 700f;
            for (int i = 0; i < ghosts.Count; i++)
            {
                var rg = ghosts[i];
                if (rg == null || !rg.gameObject.activeInHierarchy) continue;
                float d = (rg.transform.position - pos).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = rg;
                }
            }
            best?.PulseAt(pos);
        }

        static ReplayObject[] Collect(ReplayDocument doc)
        {
            var list = new List<ReplayObject>();
            foreach (var o in doc.Objects)
            {
                string t = o.Type ?? "";
                if (t.IndexOf("Projectile", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (o.Samples.Count == 0) continue;
                list.Add(o);
            }
            return list.ToArray();
        }

        static GameObject Take(int slot)
        {
            while (_pool.Count <= slot)
            {
                var go = MakeTracer();
                go.name = "NOReplay_Shell";
                go.SetActive(false);
                _pool.Add(go);
            }
            return _pool[slot];
        }

        static GameObject MakeTracer()
        {
            var go = new GameObject("NOReplay_Shell");
            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 0.22f;
            trail.startWidth = 1.6f;
            trail.endWidth = 0.35f;
            trail.minVertexDistance = 0.15f;
            trail.numCapVertices = 4;
            trail.emitting = true;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            var yellow = new Color(1f, 0.92f, 0.15f, 1f);
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            if (shader != null)
            {
                var mat = new Material(shader);
                mat.color = yellow;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", yellow * 6f);
                if (mat.HasProperty("_UnlitColor")) mat.SetColor("_UnlitColor", yellow * 6f);
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", yellow * 40f);
                if (mat.HasProperty("_EmissiveColor")) mat.SetColor("_EmissiveColor", yellow * 40f);
                mat.EnableKeyword("_EMISSION");
                trail.material = mat;
            }
            trail.startColor = yellow;
            trail.endColor = new Color(1f, 0.85f, 0.1f, 0.35f);
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "head";
            head.transform.SetParent(go.transform, false);
            head.transform.localScale = Vector3.one * 1.8f;
            var col = head.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);
            var hr = head.GetComponent<Renderer>();
            if (hr != null && shader != null)
            {
                var hm = new Material(shader);
                hm.color = yellow;
                if (hm.HasProperty("_BaseColor")) hm.SetColor("_BaseColor", yellow * 8f);
                if (hm.HasProperty("_UnlitColor")) hm.SetColor("_UnlitColor", yellow * 8f);
                if (hm.HasProperty("_EmissionColor")) hm.SetColor("_EmissionColor", yellow * 50f);
                if (hm.HasProperty("_EmissiveColor")) hm.SetColor("_EmissiveColor", yellow * 50f);
                hm.EnableKeyword("_EMISSION");
                hr.sharedMaterial = hm;
                hr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return go;
        }
    }
}
