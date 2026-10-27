using System;
using System.Collections.Generic;
using UnityEngine;

namespace NOReplay
{
    public class ReplayDocument
    {
        public string Title = "";
        public string MapId = "";
        public float TimeOfDay = -1f;
        public float Conditions = -1f;
        public float CloudHeight = -1f;
        public float WindSpeed = -1f;
        public float WindTurbulence = -1f;
        public float MoonPhase = -1f;
        public string DataRecorder = "";
        public float Duration;
        public List<ReplayObject> Objects = new List<ReplayObject>();

        public void IndexChildren()
        {
            var byId = new Dictionary<string, ReplayObject>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Objects.Count; i++)
            {
                var o = Objects[i];
                if (o == null) continue;
                o.OrdnanceLaunches = null;
                o.GunBursts = null;
                o.PartBreaks = null;
                if (string.IsNullOrEmpty(o.Id) || byId.ContainsKey(o.Id)) continue;
                byId[o.Id] = o;
            }
            var launches = new Dictionary<string, List<ReplayObject>>(StringComparer.OrdinalIgnoreCase);
            var bursts = new Dictionary<string, List<ReplayObject>>(StringComparer.OrdinalIgnoreCase);
            var parts = new Dictionary<string, List<ReplayObject>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Objects.Count; i++)
            {
                var o = Objects[i];
                if (o == null || string.IsNullOrEmpty(o.ParentId)) continue;
                if (!byId.ContainsKey(o.ParentId)) continue;
                if (o.EventTime() < 0f && !IsPartBreak(o.Type) && !IsGunBurst(o.Type)) continue;
                if (IsPartBreak(o.Type)) AddChild(parts, o.ParentId, o);
                else if (IsGunBurst(o.Type)) AddChild(bursts, o.ParentId, o);
                else if (IsOrdnanceLaunch(o.Type)) AddChild(launches, o.ParentId, o);
            }
            Stamp(byId, launches, 0);
            Stamp(byId, bursts, 1);
            Stamp(byId, parts, 2);
        }

        static void AddChild(Dictionary<string, List<ReplayObject>> map, string parentId, ReplayObject child)
        {
            if (!map.TryGetValue(parentId, out var list))
            {
                list = new List<ReplayObject>();
                map[parentId] = list;
            }
            list.Add(child);
        }

        static void Stamp(Dictionary<string, ReplayObject> byId, Dictionary<string, List<ReplayObject>> map, int kind)
        {
            foreach (var pair in map)
            {
                if (!byId.TryGetValue(pair.Key, out var parent)) continue;
                var list = pair.Value;
                list.Sort(CompareEvent);
                var arr = list.ToArray();
                if (kind == 0) parent.OrdnanceLaunches = arr;
                else if (kind == 1) parent.GunBursts = arr;
                else parent.PartBreaks = arr;
            }
        }

        static int CompareEvent(ReplayObject a, ReplayObject b)
        {
            int c = a.EventTime().CompareTo(b.EventTime());
            if (c != 0) return c;
            return string.CompareOrdinal(a.Id, b.Id);
        }

        public static bool IsGunBurst(string? type)
        {
            return (type ?? "").IndexOf("GunBurst", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsPartBreak(string? type)
        {
            return (type ?? "").IndexOf("PartBreak", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsOrdnanceLaunch(string? type)
        {
            string t = type ?? "";
            if (t.Length == 0) return false;
            if (IsGunBurst(t) || IsPartBreak(t)) return false;
            if (t.IndexOf("Flare", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (t.IndexOf("Decoy", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (t.IndexOf("Chaff", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (t.IndexOf("Projectile", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (t.IndexOf("Shell", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (t.IndexOf("Missile", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.IndexOf("Bomb", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.IndexOf("Rocket", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.IndexOf("Torpedo", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (t.IndexOf("Weapon+", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
    }

    public class ReplayObject
    {
        public string Id = "";
        public string? Name;
        public string? Type;
        public string? Coalition;
        public string? Pilot;
        public string? CallSign;
        public string? Registration;
        public string? Loadout;
        public string? Livery;
        public string? ParentId;
        public string? Weapon;
        public int GunIndex = -1;
        public float Rpm;
        public float Muzzle;
        public float Drag;
        public float Grav = 1f;
        public float Spread;
        public bool HasTurrets { get; private set; }
        public float SpawnAt = -1f;
        public float RemovedAt = -1f;
        public bool HasPart;
        public byte PartId;
        public string? PartKind;
        public string? PartPath;
        public int PartSeg = -1;
        public int PartRotor = -1;
        public float PartVx, PartVy, PartVz;
        public ReplayObject[]? OrdnanceLaunches;
        public ReplayObject[]? GunBursts;
        public ReplayObject[]? PartBreaks;
        public float NudgeX;
        public float NudgeZ;
        public List<ReplaySample> Samples = new List<ReplaySample>();

        public bool TrySample(float t, out ReplaySample s)
        {
            s = default;
            int n = Samples.Count;
            if (n == 0) return false;
            if (n == 1)
            {
                s = Samples[0];
                return true;
            }
            if (t <= Samples[0].Time)
            {
                s = Samples[0];
                return true;
            }
            if (t >= Samples[n - 1].Time)
            {
                s = Samples[n - 1];
                return true;
            }
            int lo = 0, hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (Samples[mid].Time <= t) lo = mid;
                else hi = mid;
            }
            var a = Samples[lo];
            var b = Samples[hi];
            float span = b.Time - a.Time;
            float u = span > 0.0001f ? (t - a.Time) / span : 0f;
            s = new ReplaySample
            {
                Time = t,
                X = Mathf.Lerp(a.X, b.X, u),
                Y = Mathf.Lerp(a.Y, b.Y, u),
                Z = Mathf.Lerp(a.Z, b.Z, u),
                Pitch = Mathf.LerpAngle(a.Pitch, b.Pitch, u),
                Yaw = Mathf.LerpAngle(a.Yaw, b.Yaw, u),
                Roll = Mathf.LerpAngle(a.Roll, b.Roll, u),
                HasGear = a.HasGear || b.HasGear,
                Gear = a.HasGear ? a.Gear : b.Gear,
                HasTas = a.HasTas || b.HasTas,
                Tas = a.HasTas ? a.Tas : b.Tas,
                HasThrottle = a.HasThrottle || b.HasThrottle,
                Throttle = a.HasThrottle ? a.Throttle : b.Throttle,
                HasQuat = a.HasQuat || b.HasQuat,
                Qx = a.HasQuat ? Mathf.Lerp(a.Qx, b.Qx, u) : b.Qx,
                Qy = a.HasQuat ? Mathf.Lerp(a.Qy, b.Qy, u) : b.Qy,
                Qz = a.HasQuat ? Mathf.Lerp(a.Qz, b.Qz, u) : b.Qz,
                Qw = a.HasQuat ? Mathf.Lerp(a.Qw, b.Qw, u) : b.Qw
            };
            return true;
        }

        Dictionary<string, TurretChannel>? _turrets;

        public void AddTurretKey(float time, string key, float traverse, float elevation)
        {
            if (string.IsNullOrEmpty(key)) return;
            _turrets ??= new Dictionary<string, TurretChannel>(System.StringComparer.Ordinal);
            if (!_turrets.TryGetValue(key, out var ch))
            {
                ch = new TurretChannel();
                _turrets[key] = ch;
            }
            int n = ch.Times.Count;
            if (n > 0 && Mathf.Abs(ch.Times[n - 1] - time) < 0.0001f)
            {
                ch.Traverse[n - 1] = traverse;
                ch.Elevation[n - 1] = elevation;
            }
            else
            {
                ch.Times.Add(time);
                ch.Traverse.Add(traverse);
                ch.Elevation.Add(elevation);
            }
            if (ch.Times.Count > 1
                || Mathf.Abs(traverse) > TurretStillDegrees
                || Mathf.Abs(elevation) > TurretStillDegrees)
                ch.Moves = true;
            HasTurrets = true;
            if (ch.Moves) HasMovingTurrets = true;
        }

        /// <summary>
        /// True when some turret channel moved, or holds a non-zero pose.
        /// One 0.00|0.00 spawn sample does not count.
        /// </summary>
        public bool HasMovingTurrets { get; private set; }

        const float TurretStillDegrees = 0.05f;

        /// <summary>
        /// Exact key, then a path suffix either way, then the only channel
        /// when the unit recorded a single turret.
        /// </summary>
        TurretChannel? FindTurretChannel(string key)
        {
            if (_turrets == null || string.IsNullOrEmpty(key)) return null;
            if (_turrets.TryGetValue(key, out var exact)) return exact;
            foreach (var pair in _turrets)
            {
                string k = pair.Key;
                if (string.IsNullOrEmpty(k)) continue;
                if (k.EndsWith("/" + key, System.StringComparison.OrdinalIgnoreCase)
                    || key.EndsWith("/" + k, System.StringComparison.OrdinalIgnoreCase))
                    return pair.Value;
            }
            if (_turrets.Count == 1)
            {
                foreach (var pair in _turrets)
                    return pair.Value;
            }
            return null;
        }

        /// <summary>
        /// A channel owns the visual pose when it has several samples or any
        /// angle away from zero. One static (0, 0) baseline does not.
        /// </summary>
        public bool TurretChannelMoves(string key)
        {
            var ch = FindTurretChannel(key);
            return ch != null && ch.Moves;
        }

        public bool TryTurret(string key, float time, out float traverse, out float elevation)
        {
            traverse = 0f;
            elevation = 0f;
            var ch = FindTurretChannel(key);
            if (ch == null) return false;
            int n = ch.Times.Count;
            if (n == 0) return false;
            if (n == 1 || time <= ch.Times[0])
            {
                traverse = ch.Traverse[0];
                elevation = ch.Elevation[0];
                return true;
            }
            if (time >= ch.Times[n - 1])
            {
                traverse = ch.Traverse[n - 1];
                elevation = ch.Elevation[n - 1];
                return true;
            }
            int lo = 0, hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (ch.Times[mid] <= time) lo = mid;
                else hi = mid;
            }
            float span = ch.Times[hi] - ch.Times[lo];
            float u = span > 0.0001f ? (time - ch.Times[lo]) / span : 0f;
            traverse = Mathf.LerpAngle(ch.Traverse[lo], ch.Traverse[hi], u);
            elevation = Mathf.LerpAngle(ch.Elevation[lo], ch.Elevation[hi], u);
            return true;
        }

        sealed class TurretChannel
        {
            public readonly List<float> Times = new List<float>();
            public readonly List<float> Traverse = new List<float>();
            public readonly List<float> Elevation = new List<float>();
            public bool Moves;
        }

        public float EventTime()
        {
            if (SpawnAt >= 0f) return SpawnAt;
            if (Samples.Count > 0) return Samples[0].Time;
            return -1f;
        }

        public float BurstEnd()
        {
            if (RemovedAt >= 0f) return RemovedAt;
            if (Samples.Count > 0) return Samples[Samples.Count - 1].Time;
            float start = EventTime();
            return start >= 0f ? start : 0f;
        }

        public static int CountAtOrBefore(ReplayObject[] items, float time)
        {
            int lo = 0, hi = items.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (items[mid].EventTime() <= time) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        public bool IsParkedStatic()
        {
            int n = Samples.Count;
            if (n == 0) return false;
            float maxTas = 0f;
            float x0 = Samples[0].X, z0 = Samples[0].Z;
            float maxD = 0f;
            int step = n > 40 ? n / 40 : 1;
            for (int i = 0; i < n; i += step)
            {
                var s = Samples[i];
                if (s.HasTas && s.Tas > maxTas) maxTas = s.Tas;
                float dx = s.X - x0, dz = s.Z - z0;
                float d = dx * dx + dz * dz;
                if (d > maxD) maxD = d;
            }
            return maxTas < 8f && maxD < 400f;
        }
    }

    public struct ReplaySample
    {
        public float Time;
        public float X, Y, Z;
        public float Pitch, Yaw, Roll;
        public bool HasQuat;
        public float Qx, Qy, Qz, Qw;
        public bool HasGear;
        public float Gear;
        public bool HasTas;
        public float Tas;
        public bool HasThrottle;
        public float Throttle;

        public ReplaySample Clone() => this;
    }
}