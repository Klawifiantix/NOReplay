using System;
using System.Collections.Generic;
using UnityEngine;

namespace NOReplay
{
    static class BurstReplay
    {
        static ReplayObject[] _bursts = Array.Empty<ReplayObject>();
        static ReplayDocument? _doc;
        static readonly Dictionary<string, ReplayObject> _byId = new Dictionary<string, ReplayObject>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, ReplayGhost> _ghostById = new Dictionary<string, ReplayGhost>(StringComparer.Ordinal);
        static readonly Dictionary<string, float> _nextShot = new Dictionary<string, float>(StringComparer.Ordinal);
        static int _ghostMapFrame = -1;
        static float _lastTime = -1f;
        static bool _wasPaused;
        static bool _shown;

        public static void Tick(ReplayDocument doc, float time)
        {
            bool paused = Plugin.Clock.Paused;
            float expect = Mathf.Max(0.02f, Mathf.Abs(Plugin.Clock.Speed) * Time.unscaledDeltaTime);
            bool rewound = _lastTime >= 0f && time < _lastTime - 0.02f;
            bool jumped = _lastTime >= 0f && time > _lastTime + Mathf.Max(0.35f, expect * 8f);
            if (!ReferenceEquals(doc, _doc))
            {
                Bind(doc);
                rewound = true;
            }
            if (rewound || jumped)
            {
                HaltAll(true);
                _nextShot.Clear();
                _wasPaused = paused;
                _lastTime = time;
                return;
            }
            if (paused && !_wasPaused)
                HaltAll(false);
            else if (!paused && _wasPaused)
                ResumeAll();
            _wasPaused = paused;
            _lastTime = time;
            if (paused) return;

            for (int i = 0; i < _bursts.Length; i++)
            {
                var o = _bursts[i];
                if (o.Samples.Count == 0) continue;
                float start = o.SpawnAt >= 0f ? o.SpawnAt : o.Samples[0].Time;
                float end = o.RemovedAt >= 0f ? o.RemovedAt : o.Samples[o.Samples.Count - 1].Time;
                if (time < start || time > end + 0.05f) continue;
                var ghost = GhostFor(FindParent(o));
                if (ghost == null || !ghost.gameObject.activeInHierarchy || ghost.IsWrecked || !ghost.IsTrackAlive())
                {
                    _nextShot[o.Id] = time;
                    continue;
                }
                if (o.TrySample(time, out var aim))
                {
                    var dir = AimDir(aim);
                    if (dir.sqrMagnitude > 0.01f) ghost.AimGun(dir);
                }
                if (!Plugin.AudioNear(ghost.gameObject, 3500f))
                {
                    _nextShot[o.Id] = time;
                    continue;
                }
                float rpm = o.Rpm > 1f ? o.Rpm : 600f;
                float step = Mathf.Clamp(60f / rpm, 0.008f, 0.5f);
                if (!_nextShot.TryGetValue(o.Id, out float next) || next < start)
                    next = Mathf.Max(start, time - step);
                if (next < time - step * 3f)
                    next = time;
                int spawned = 0;
                while (next <= time && next <= end && spawned < 16)
                {
                    ghost.FireRecordedGun(o, end);
                    next += step;
                    spawned++;
                }
                _nextShot[o.Id] = next;
                if (!_shown && spawned > 0)
                {
                    _shown = true;
                    Plugin.Log.LogInfo("Kanonenstoß nativ " + o.Id + " rpm=" + rpm.ToString("0") + " gun=" + o.GunIndex + " " + (o.Weapon ?? ""));
                }
            }
        }

        static Vector3 AimDir(ReplaySample aim)
        {
            float yaw = aim.Yaw * Mathf.Deg2Rad;
            float pitch = -aim.Pitch * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch));
        }

        static void Bind(ReplayDocument doc)
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
            Plugin.Log.LogInfo("Kanonenstöße im Replay: " + _bursts.Length);
        }

        static void HaltAll(bool clearBullets)
        {
            var list = Plugin.GhostComps;
            for (int i = 0; i < list.Count; i++)
            {
                var g = list[i];
                if (g != null) g.HaltGuns(clearBullets);
            }
        }

        static void ResumeAll()
        {
            var list = Plugin.GhostComps;
            for (int i = 0; i < list.Count; i++)
            {
                var g = list[i];
                if (g != null) g.ResumeGuns();
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
    }
}
