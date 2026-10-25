using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NOReplay
{
    static class BurstReplay
    {
        static ReplayObject[] _bursts = Array.Empty<ReplayObject>();
        static ReplayDocument? _doc;
        static readonly List<GameObject> _pool = new List<GameObject>();
        static readonly Dictionary<string, ReplayObject> _byId = new Dictionary<string, ReplayObject>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, ReplayGhost> _ghostById = new Dictionary<string, ReplayGhost>(StringComparer.Ordinal);
        static int _ghostMapFrame = -1;
        static int _slotGen = 1;
        static readonly int[] _slotBurst = new int[48];
        static readonly int[] _slotShot = new int[48];
        static readonly int[] _slotGenAt = new int[48];
        static bool _shown;

        static readonly HashSet<string> _heard = new HashSet<string>();

        static float _lastTime = -1f;
        static bool _wasPaused;

        public static void Tick(ReplayDocument doc, float time)
        {
            bool paused = Plugin.Clock.Paused;
            bool seeked = _lastTime >= 0f && Mathf.Abs(time - _lastTime) > 0.5f;
            if ((paused && !_wasPaused) || seeked)
            {
                if (paused && !_wasPaused)
                    Plugin.Log.LogInfo("Pause: stopping burst audio");
                EndAllBurstAudio();
                _heard.Clear();
                _slotGen++;
            }
            _wasPaused = paused;
            _lastTime = time;
            if (paused) return;

            if (!ReferenceEquals(doc, _doc))
            {
                _doc = doc;
                var list = new List<ReplayObject>();
                var objs = doc.Objects;
                _byId.Clear();
                for (int n = 0; n < objs.Count; n++)
                {
                    var obj = objs[n];
                    if (obj == null || string.IsNullOrEmpty(obj.Id)) continue;
                    if (!_byId.ContainsKey(obj.Id))
                        _byId[obj.Id] = obj;
                    if ((obj.Type ?? "").IndexOf("GunBurst", StringComparison.OrdinalIgnoreCase) >= 0 && obj.Samples.Count > 0)
                        list.Add(obj);
                }
                _bursts = list.ToArray();
                _heard.Clear();
                foreach (var go in _pool)
                    if (go != null) UnityEngine.Object.Destroy(go);
                _pool.Clear();
                _slotGen++;
                Plugin.Log.LogInfo("Kanonenstöße im Replay: " + _bursts.Length);
            }
            int slot = 0;
            for (int i = 0; i < _bursts.Length && slot < 48; i++)
            {
                var o = _bursts[i];
                float start = o.SpawnAt >= 0f ? o.SpawnAt : o.Samples[0].Time;
                float end = o.RemovedAt >= 0f ? o.RemovedAt : o.Samples[o.Samples.Count - 1].Time;
                bool inWindow = time >= start && time <= end + 1.6f;
                bool endAudio = time > end && _heard.Contains(o.Id);
                if (!inWindow && !endAudio) continue;
                var shooter = FindParent(o);
                var ghost = GhostFor(shooter);
                if (endAudio)
                {
                    _heard.Remove(o.Id);
                    ghost?.EndBurstAudio();
                }
                if (!inWindow) continue;
                // Dead / wrecked units must not produce gun effects.
                if (ghost != null && (ghost.IsWrecked || !ghost.IsTrackAlive()))
                    continue;
                float rpm = o.Rpm > 1f ? o.Rpm : 750f;
                float speed = o.Muzzle > 50f ? o.Muzzle : 900f;
                float grav = o.Grav > 0f ? o.Grav : 1f;
                float step = Mathf.Max(0.04f, 60f / rpm);
                float fired = start;
                float window = time - 1.6f;
                if (window > start)
                {
                    // One step early so float rounding cannot skip a round that is still visible.
                    float steps = Mathf.Floor((window - start) / step);
                    if (steps > 1f)
                        fired = start + (steps - 1f) * step;
                }
                bool pulsed = false;
                for (; fired <= end && slot < 48; fired += step)
                {
                    float age = time - fired;
                    if (age < 0f || age > 1.6f) continue;
                    if (!o.TrySample(fired, out var aim)) continue;
                    Vector3 origin;
                    if (ghost != null)
                        origin = ghost.MuzzlePos();
                    else if (shooter != null && shooter.TrySample(fired, out var src))
                        origin = Plugin.GlobalToLocal(src.X, src.Y, src.Z);
                    else
                        origin = Plugin.GlobalToLocal(aim.X, aim.Y, aim.Z);
                    float yaw = aim.Yaw * Mathf.Deg2Rad;
                    float pitch = -aim.Pitch * Mathf.Deg2Rad;
                    var dir = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch));
                    ghost?.AimGun(dir);
                    if (dir.sqrMagnitude < 0.01f) continue;
                    float drop = 0.5f * 9.81f * grav * age * age;
                    var pos = origin + dir.normalized * (speed * age) + Vector3.down * drop;
                    var go = Take(slot);
                    var trail = go.GetComponent<TrailRenderer>();
                    int shot = Mathf.RoundToInt((fired - start) / step);
                    if (trail != null && (_slotGenAt[slot] != _slotGen || _slotBurst[slot] != i || _slotShot[slot] != shot))
                    {
                        trail.emitting = false;
                        trail.time = 0f;
                        trail.Clear();
                        go.SetActive(false);
                        go.transform.position = pos;
                        go.SetActive(true);
                        trail.Clear();
                        trail.time = 0.12f;
                        trail.emitting = true;
                        _slotGenAt[slot] = _slotGen;
                        _slotBurst[slot] = i;
                        _slotShot[slot] = shot;
                    }
                    go.transform.position = pos;
                    go.SetActive(true);
                    slot++;
                    if (!pulsed && ghost != null && time <= end)
                    {
                        pulsed = true;
                        ghost.HoldBurstAudio();
                        if (Plugin.AudioNear(ghost.gameObject, 550f) && _heard.Add(o.Id))
                            ghost.BeginBurstAudio(o.Weapon);
                    }
                    else if (pulsed && time > end)
                    {
                        ghost?.EndBurstAudio();
                        _heard.Remove(o.Id);
                    }
                    if (!_shown)
                    {
                        _shown = true;
                        Plugin.Log.LogInfo("Kanonenstoß " + o.Id + " rpm=" + rpm.ToString("0") + " v=" + speed.ToString("0") + " @ " + origin);
                    }
                }
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
                if (i < _slotGenAt.Length) _slotGenAt[i] = 0;
            }
        }

        static void EndAllBurstAudio()
        {
            var list = Plugin.GhostComps;
            for (int i = 0; i < list.Count; i++)
            {
                var g = list[i];
                if (g != null) g.EndBurstAudio();
            }
        }

        static ReplayGhost? GhostFor(ReplayObject? shooter)
        {
            if (shooter == null || string.IsNullOrEmpty(shooter.Id)) return null;
            if (_ghostMapFrame != Time.frameCount)
            {
                _ghostMapFrame = Time.frameCount;
                _ghostById.Clear();
                var list = Plugin.GhostComps;
                for (int i = 0; i < list.Count; i++)
                {
                    var g = list[i];
                    if (g == null || g.Track == null || !g.gameObject.activeInHierarchy) continue;
                    string id = g.Track.Id;
                    if (string.IsNullOrEmpty(id) || _ghostById.ContainsKey(id)) continue;
                    _ghostById[id] = g;
                }
            }
            _ghostById.TryGetValue(shooter.Id, out var found);
            return found;
        }

        static ReplayObject? FindParent(ReplayObject burst)
        {
            if (string.IsNullOrEmpty(burst.ParentId)) return null;
            _byId.TryGetValue(burst.ParentId, out var o);
            return o;
        }

        static GameObject Take(int slot)
        {
            while (_pool.Count <= slot)
            {
                var go = new GameObject("NOReplay_Burst");
                var trail = go.AddComponent<TrailRenderer>();
                trail.time = 0.12f;
                trail.startWidth = 0.45f;
                trail.endWidth = 0.05f;
                trail.minVertexDistance = 0.4f;
                trail.autodestruct = false;
                trail.emitting = false;
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var yellow = new Color(1f, 0.9f, 0.2f, 1f);
                var shader = Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    var mat = new Material(shader);
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", yellow * 6f);
                    if (mat.HasProperty("_EmissiveColor")) mat.SetColor("_EmissiveColor", yellow * 12f);
                    mat.EnableKeyword("_EMISSION");
                    trail.material = mat;
                }
                trail.startColor = yellow;
                trail.endColor = new Color(1f, 0.8f, 0.1f, 0.2f);
                go.SetActive(false);
                _pool.Add(go);
            }
            return _pool[slot];
        }
    }
}
