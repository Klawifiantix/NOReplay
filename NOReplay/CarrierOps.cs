using System;
using System.Collections.Generic;

namespace NOReplay
{
    /// <summary>
    /// Tailhook and arresting-wire timing as a function of replay time.
    /// No Unity types, so a seek can be checked without the game assemblies.
    /// The hook copies TailHook.CheckDeployConditions (once a second) and the
    /// 0.5/s ramp. A trap is a sharp stop on the deck with the hook down.
    /// </summary>
    public static class CarrierOps
    {
        public const float RampPerSecond = 0.5f;
        public const float CheckPeriod = 1f;
        public const float DeployAglMin = 30f;
        public const float DeployAglMax = 120f;
        public const float DeploySpeedMax = 100f;
        public const float RetractFast = 100f;
        public const float RetractSlow = 2f;
        public const float CableReturnSpeed = 4f;
        public const float MaxHookSeconds = 5f;
        public const float MinHookSeconds = 1f;
        public const float TrapWindow = 4.5f;
        public const float TrapEntrySpeed = 40f;
        public const float TrapStopSpeed = 5f;
        public const float TrapDrop = 40f;
        public const float TrapBite = 12f;
        public const float HookDown = 0.45f;
        public const float DeckAgl = 12f;
        public const float WireReach = 55f;
        public const float HookAft = 7f;

        public struct HookSample
        {
            public float Time;
            public float Speed;
            public float Agl;
            public float Y;
            public bool HasAgl;
            public bool HasTailhook;
            public float Tailhook;
        }

        public struct HookSeg
        {
            public float Time;
            public float Amount;
            public float Rate;
        }

        public struct LocalSample
        {
            public float Time;
            public float X, Y, Z;
            public float Speed;
            public float Hook;
            public float Agl;
            public bool HasAgl;
        }

        public struct WireLine
        {
            public int Number;
            public float Ax, Ay, Az;
            public float Bx, By, Bz;
            public float MaxExtension;
        }

        public struct TrapHit
        {
            public float Catch;
            public float Release;
            public int Wire;
            public float X, Z;
        }

        public static bool LoadoutHasTailhook(string? loadout)
        {
            if (string.IsNullOrEmpty(loadout)) return false;
            return loadout.IndexOf("TailHook_", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool SameId(string? a, string? b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Event=Trap|aircraft|carrier|wire or Event=Untrap|...
        /// </summary>
        public static bool TryParseTrapEvent(ReadOnlySpan<char> value, out bool release, out string aircraft, out string carrier, out int wire)
        {
            release = false;
            aircraft = "";
            carrier = "";
            wire = 0;
            int p1 = value.IndexOf('|');
            if (p1 <= 0) return false;
            var kind = value.Slice(0, p1);
            bool trap = kind.Equals("Trap".AsSpan(), StringComparison.OrdinalIgnoreCase);
            bool un = kind.Equals("Untrap".AsSpan(), StringComparison.OrdinalIgnoreCase);
            if (!trap && !un) return false;
            var rest = value.Slice(p1 + 1);
            int p2 = rest.IndexOf('|');
            if (p2 <= 0) return false;
            var ac = rest.Slice(0, p2).Trim();
            var rest2 = rest.Slice(p2 + 1);
            int p3 = rest2.IndexOf('|');
            if (p3 <= 0) return false;
            var car = rest2.Slice(0, p3).Trim();
            var wireSpan = rest2.Slice(p3 + 1).Trim();
            if (ac.Length == 0 || car.Length == 0) return false;
            if (!TryInt(wireSpan, out wire)) return false;
            release = un;
            aircraft = ac.ToString();
            carrier = car.ToString();
            return true;
        }

        public static bool HasRecordedTailhook(HookSample[] samples)
        {
            if (samples == null) return false;
            for (int i = 0; i < samples.Length; i++)
                if (samples[i].HasTailhook) return true;
            return false;
        }

        public static HookSeg[] BuildHook(HookSample[] samples)
        {
            if (samples == null || samples.Length == 0)
                return Array.Empty<HookSeg>();
            if (HasRecordedTailhook(samples))
                return BuildRecorded(samples);
            return BuildInferred(samples);
        }

        public static float AmountAt(HookSeg[] segs, float time)
        {
            if (segs == null || segs.Length == 0) return 0f;
            if (time <= segs[0].Time) return Clamp01(segs[0].Amount);
            int lo = 0;
            int hi = segs.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (segs[mid].Time <= time) lo = mid;
                else hi = mid - 1;
            }
            var s = segs[lo];
            return Clamp01(s.Amount + s.Rate * (time - s.Time));
        }

        public static TrapHit[] DetectTraps(LocalSample[] path, WireLine[]? wires)
        {
            var hits = new List<TrapHit>();
            if (path == null || path.Length < 3) return hits.ToArray();
            int n = path.Length;
            for (int i = 0; i < n - 1; i++)
            {
                if (path[i].Hook < HookDown || path[i].Speed < TrapEntrySpeed) continue;
                if (!OnDeck(path[i], wires)) continue;
                int slow = -1;
                for (int k = i + 1; k < n; k++)
                {
                    if (path[k].Time - path[i].Time > TrapWindow) break;
                    if (path[k].Speed < TrapStopSpeed)
                    {
                        slow = k;
                        break;
                    }
                }
                if (slow < 0) continue;
                if (path[i].Speed - path[slow].Speed < TrapDrop) continue;

                int knee = i;
                bool bitten = false;
                for (int k = i + 1; k <= slow; k++)
                {
                    float dt = path[k].Time - path[k - 1].Time;
                    if (dt < 0.02f) continue;
                    float decel = (path[k - 1].Speed - path[k].Speed) / dt;
                    if (decel > TrapBite && path[k].Speed < path[i].Speed - 6f)
                    {
                        knee = k - 1;
                        bitten = true;
                        break;
                    }
                }
                if (!bitten) continue;
                if (!OnDeck(path[knee], wires) && !OnDeck(path[Math.Min(knee + 1, slow)], wires))
                    continue;

                float catchT = path[knee].Time;
                int wire = 0;
                float crossT = catchT;
                if (wires != null && wires.Length > 0 && TryPickWire(path, i, slow, catchT, wires, out int number, out float when))
                {
                    wire = number;
                    crossT = when;
                    if (crossT < path[i].Time) crossT = path[i].Time;
                    if (crossT > path[slow].Time) crossT = catchT;
                }
                else if (wires != null && wires.Length > 0)
                {
                    wire = NearestWire(wires, path[knee].X, path[knee].Z);
                }

                float release = path[slow].Time;
                if (release < crossT + MinHookSeconds) release = crossT + MinHookSeconds;
                if (release > crossT + MaxHookSeconds) release = crossT + MaxHookSeconds;
                float ext = ExtensionRelease(path, wires, wire, crossT, release);
                if (ext > crossT + 0.3f && ext < release) release = ext;

                hits.Add(new TrapHit
                {
                    Catch = crossT,
                    Release = release,
                    Wire = wire,
                    X = path[knee].X,
                    Z = path[knee].Z
                });
                i = slow;
            }
            return hits.ToArray();
        }

        public static float ReturnAlong(float elapsed, float gap)
        {
            if (elapsed <= 0f || gap <= 0f) return 0f;
            float moved = CableReturnSpeed * elapsed;
            return moved >= gap ? gap : moved;
        }

        static HookSeg[] BuildInferred(HookSample[] samples)
        {
            float t0 = samples[0].Time;
            float t1 = samples[samples.Length - 1].Time;
            bool anyAgl = false;
            for (int i = 0; i < samples.Length; i++)
                if (samples[i].HasAgl) { anyAgl = true; break; }
            float ground = 0f;
            if (!anyAgl)
                ground = GroundY(samples);

            var segs = new List<HookSeg>(8);
            segs.Add(new HookSeg { Time = t0, Amount = 0f, Rate = 0f });
            bool deployed = false;
            float segTime = t0;
            float segAmount = 0f;
            float segRate = 0f;
            int steps = (int)MathF.Floor((t1 - t0) / CheckPeriod + 0.001f);
            if (steps < 0) steps = 0;
            if (steps > 20000) steps = 20000;
            for (int s = 0; s <= steps; s++)
            {
                float check = t0 + s * CheckPeriod;
                if (check > t1 + 0.001f) break;
                SampleAt(samples, check, anyAgl, ground, out float speed, out float agl, out float vy);
                bool state = deployed;
                if (agl > DeployAglMin && agl < DeployAglMax && speed < DeploySpeedMax && vy < 0f && !state)
                    state = true;
                if ((speed > RetractFast || speed < RetractSlow) && state)
                    state = false;
                if (state == deployed) continue;
                float dt = check - segTime;
                float amount = Clamp01(segAmount + segRate * dt);
                float rate = state ? RampPerSecond : -RampPerSecond;
                segs.Add(new HookSeg { Time = check, Amount = amount, Rate = rate });
                segTime = check;
                segAmount = amount;
                segRate = rate;
                deployed = state;
            }
            return segs.ToArray();
        }

        static HookSeg[] BuildRecorded(HookSample[] samples)
        {
            var keys = new List<HookSeg>();
            for (int i = 0; i < samples.Length; i++)
            {
                if (!samples[i].HasTailhook) continue;
                float amount = Clamp01(samples[i].Tailhook);
                if (keys.Count > 0 && MathF.Abs(keys[keys.Count - 1].Amount - amount) < 0.0001f)
                    continue;
                keys.Add(new HookSeg { Time = samples[i].Time, Amount = amount, Rate = 0f });
            }
            if (keys.Count == 0)
                return new[] { new HookSeg { Time = samples[0].Time, Amount = 0f, Rate = 0f } };
            bool holdUntilFirst = keys[0].Time > samples[0].Time + 0.02f;
            if (holdUntilFirst)
                keys.Insert(0, new HookSeg { Time = samples[0].Time, Amount = 0f, Rate = 0f });
            int rampFrom = holdUntilFirst ? 1 : 0;
            for (int i = rampFrom; i < keys.Count - 1; i++)
            {
                var a = keys[i];
                var b = keys[i + 1];
                float dt = b.Time - a.Time;
                a.Rate = dt > 0.0001f ? (b.Amount - a.Amount) / dt : 0f;
                keys[i] = a;
            }
            return keys.ToArray();
        }

        static float GroundY(HookSample[] samples)
        {
            float minY = float.MaxValue;
            float slowY = float.MaxValue;
            bool slow = false;
            for (int i = 0; i < samples.Length; i++)
            {
                float y = samples[i].Y;
                if (y < minY) minY = y;
                if (samples[i].Speed < RetractSlow + 3f && y < slowY)
                {
                    slowY = y;
                    slow = true;
                }
            }
            if (slow) return slowY;
            return minY == float.MaxValue ? 0f : minY;
        }

        static void SampleAt(HookSample[] samples, float time, bool anyAgl, float ground, out float speed, out float agl, out float vy)
        {
            int n = samples.Length;
            if (time <= samples[0].Time)
            {
                speed = samples[0].Speed;
                agl = AglOf(samples[0], anyAgl, ground);
                vy = Vertical(samples, time);
                return;
            }
            if (time >= samples[n - 1].Time)
            {
                speed = samples[n - 1].Speed;
                agl = AglOf(samples[n - 1], anyAgl, ground);
                vy = Vertical(samples, time);
                return;
            }
            int lo = 0;
            int hi = n - 1;
            while (lo + 1 < hi)
            {
                int mid = (lo + hi) >> 1;
                if (samples[mid].Time <= time) lo = mid;
                else hi = mid;
            }
            var a = samples[lo];
            var b = samples[hi];
            float span = b.Time - a.Time;
            float u = span > 0.0001f ? (time - a.Time) / span : 0f;
            speed = a.Speed + (b.Speed - a.Speed) * u;
            if (anyAgl && a.HasAgl && b.HasAgl)
                agl = a.Agl + (b.Agl - a.Agl) * u;
            else if (a.HasAgl)
                agl = a.Agl;
            else if (b.HasAgl)
                agl = b.Agl;
            else
                agl = (a.Y + (b.Y - a.Y) * u) - ground;
            vy = Vertical(samples, time);
        }

        static float AglOf(HookSample s, bool anyAgl, float ground)
        {
            if (s.HasAgl) return s.Agl;
            if (!anyAgl) return s.Y - ground;
            return 0f;
        }

        static float Vertical(HookSample[] samples, float time)
        {
            float y0 = YAt(samples, time - 0.25f);
            float y1 = YAt(samples, time + 0.25f);
            return (y1 - y0) / 0.5f;
        }

        static float YAt(HookSample[] samples, float time)
        {
            int n = samples.Length;
            if (time <= samples[0].Time) return samples[0].Y;
            if (time >= samples[n - 1].Time) return samples[n - 1].Y;
            int lo = 0;
            int hi = n - 1;
            while (lo + 1 < hi)
            {
                int mid = (lo + hi) >> 1;
                if (samples[mid].Time <= time) lo = mid;
                else hi = mid;
            }
            var a = samples[lo];
            var b = samples[hi];
            float span = b.Time - a.Time;
            float u = span > 0.0001f ? (time - a.Time) / span : 0f;
            return a.Y + (b.Y - a.Y) * u;
        }

        static bool OnDeck(LocalSample s, WireLine[]? wires)
        {
            if (s.Hook < HookDown) return false;
            bool low = s.HasAgl ? s.Agl < DeckAgl : (s.Y > 6f && s.Y < 22f);
            if (!low) return false;
            if (wires != null && wires.Length > 0)
            {
                float best = float.MaxValue;
                for (int i = 0; i < wires.Length; i++)
                {
                    float d = DistSeg(s.X, s.Z, wires[i].Ax, wires[i].Az, wires[i].Bx, wires[i].Bz);
                    if (d < best) best = d;
                }
                return best <= WireReach;
            }
            if (s.X > 80f || s.X < -80f) return false;
            if (s.Z < -180f || s.Z > 200f) return false;
            return true;
        }

        static bool TryPickWire(LocalSample[] path, int from, int to, float knee, WireLine[] wires, out int number, out float when)
        {
            number = 0;
            when = knee;
            float best = float.MaxValue;
            bool found = false;
            for (int i = from; i < to; i++)
            {
                var a = path[i];
                var b = path[i + 1];
                for (int w = 0; w < wires.Length; w++)
                {
                    if (!Cross(a.X, a.Z, b.X, b.Z, wires[w].Ax, wires[w].Az, wires[w].Bx, wires[w].Bz, out float u))
                        continue;
                    float t = a.Time + (b.Time - a.Time) * u;
                    float dt = t - knee;
                    if (dt < 0f) dt = -dt;
                    if (dt < best)
                    {
                        best = dt;
                        number = wires[w].Number;
                        when = t;
                        found = true;
                    }
                }
            }
            if (!found || best > 1.5f) return false;
            return true;
        }

        static int NearestWire(WireLine[] wires, float x, float z)
        {
            int number = wires[0].Number;
            float best = float.MaxValue;
            for (int i = 0; i < wires.Length; i++)
            {
                float d = DistSeg(x, z, wires[i].Ax, wires[i].Az, wires[i].Bx, wires[i].Bz);
                if (d < best)
                {
                    best = d;
                    number = wires[i].Number;
                }
            }
            return number;
        }

        static float ExtensionRelease(LocalSample[] path, WireLine[]? wires, int wire, float catchT, float release)
        {
            if (wires == null || wire == 0) return -1f;
            WireLine line = default;
            bool have = false;
            for (int i = 0; i < wires.Length; i++)
            {
                if (wires[i].Number == wire && wires[i].MaxExtension > 10f)
                {
                    line = wires[i];
                    have = true;
                    break;
                }
            }
            if (!have) return -1f;
            float relaxed = Dist3(line.Ax, line.Ay, line.Az, line.Bx, line.By, line.Bz);
            for (int i = 0; i < path.Length - 1; i++)
            {
                if (path[i + 1].Time < catchT) continue;
                if (path[i].Time > release) break;
                float ext = Dist3(path[i].X, path[i].Y, path[i].Z, line.Ax, line.Ay, line.Az)
                          + Dist3(path[i].X, path[i].Y, path[i].Z, line.Bx, line.By, line.Bz)
                          - relaxed;
                if (ext > line.MaxExtension) return path[i].Time;
            }
            return -1f;
        }

        public static void Rotate(float qx, float qy, float qz, float qw, float vx, float vy, float vz, out float x, out float y, out float z)
        {
            float tx = 2f * (qy * vz - qz * vy);
            float ty = 2f * (qz * vx - qx * vz);
            float tz = 2f * (qx * vy - qy * vx);
            x = vx + qw * tx + (qy * tz - qz * ty);
            y = vy + qw * ty + (qz * tx - qx * tz);
            z = vz + qw * tz + (qx * ty - qy * tx);
        }

        public static void InverseRotate(float qx, float qy, float qz, float qw, float vx, float vy, float vz, out float x, out float y, out float z)
        {
            Rotate(-qx, -qy, -qz, qw, vx, vy, vz, out x, out y, out z);
        }

        static bool Cross(float ax, float az, float bx, float bz, float cx, float cz, float dx, float dz, out float u)
        {
            u = 0f;
            float rx = bx - ax;
            float rz = bz - az;
            float sx = dx - cx;
            float sz = dz - cz;
            float den = rx * sz - rz * sx;
            if (den < 1e-4f && den > -1e-4f) return false;
            float qpx = cx - ax;
            float qpz = cz - az;
            float tu = (qpx * sz - qpz * sx) / den;
            float tv = (qpx * rz - qpz * rx) / den;
            if (tu < -0.02f || tu > 1.02f || tv < -0.05f || tv > 1.05f) return false;
            u = tu < 0f ? 0f : (tu > 1f ? 1f : tu);
            return true;
        }

        static float DistSeg(float px, float pz, float ax, float az, float bx, float bz)
        {
            float abx = bx - ax;
            float abz = bz - az;
            float ab2 = abx * abx + abz * abz;
            float u = ab2 < 1e-6f ? 0f : ((px - ax) * abx + (pz - az) * abz) / ab2;
            if (u < 0f) u = 0f;
            else if (u > 1f) u = 1f;
            float dx = ax + abx * u - px;
            float dz = az + abz * u - pz;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        static float Dist3(float ax, float ay, float az, float bx, float by, float bz)
        {
            float dx = ax - bx;
            float dy = ay - by;
            float dz = az - bz;
            return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }

        static bool TryInt(ReadOnlySpan<char> raw, out int value)
        {
            value = 0;
            if (raw.Length == 0) return false;
            int sign = 1;
            int i = 0;
            if (raw[0] == '-') { sign = -1; i++; }
            if (i >= raw.Length) return false;
            int n = 0;
            for (; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c < '0' || c > '9') return false;
                n = n * 10 + (c - '0');
            }
            value = n * sign;
            return true;
        }
    }
}
