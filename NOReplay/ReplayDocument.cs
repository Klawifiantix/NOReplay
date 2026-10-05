using System.Collections.Generic;
using UnityEngine;

namespace NOReplay
{
    public class ReplayDocument
    {
        public string Title = "";
        public string MapId = "";
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
        public float Rpm;
        public float Muzzle;
        public float Drag;
        public float Grav = 1f;
        public float Spread;
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
                Throttle = a.HasThrottle ? a.Throttle : b.Throttle
            };
            return true;
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
        public bool HasGear;
        public float Gear;
        public bool HasTas;
        public float Tas;
        public bool HasThrottle;
        public float Throttle;

        public ReplaySample Clone() => this;
    }
}