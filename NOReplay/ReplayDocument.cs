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