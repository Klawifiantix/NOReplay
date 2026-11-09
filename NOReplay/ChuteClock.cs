using System;
using System.Collections.Generic;
using System.Globalization;

namespace NOReplay
{
    /// <summary>
    /// Parachute canopy, the cargo rig, the pilot animation state, the
    /// detached seat and the seat-fire edge. An empty channel stays closed,
    /// so an old file does not invent a chute.
    /// </summary>
    public static class ChuteClock
    {
        public enum Kind : byte
        {
            Canopy = 1,
            Rig = 2,
            Pilot = 3,
            Seat = 4,
            Fire = 5
        }

        public struct Event
        {
            public float Time;
            public string Key;
            public Kind Kind;
            public bool On;
            public bool HasPose;
            public int State;
            public float X, Y, Z;
            public float Qx, Qy, Qz, Qw;
            public float Sx, Sy, Sz;
            public float Lx, Ly, Lz;
            public float Wrinkle, Strength;
        }

        public struct View
        {
            public bool On;
            public bool HasPose;
            public int State;
            public float X, Y, Z;
            public float Qx, Qy, Qz, Qw;
            public float Sx, Sy, Sz;
            public float Lx, Ly, Lz;
            public float Wrinkle, Strength;
        }

        public static int Parse(string? text, float time, List<Event> dest)
        {
            if (dest == null || string.IsNullOrEmpty(text)) return 0;
            int added = 0;
            int i = 0;
            while (i <= text.Length)
            {
                int rel = i < text.Length ? text.IndexOf(';', i) : -1;
                int end = rel < 0 ? text.Length : rel;
                if (end > i && TrySegment(text.Substring(i, end - i), time, out var ev))
                {
                    dest.Add(ev);
                    added++;
                }
                if (rel < 0) break;
                i = rel + 1;
            }
            return added;
        }

        public static bool TrySegment(string seg, float time, out Event ev)
        {
            ev = default;
            if (string.IsNullOrEmpty(seg)) return false;
            int colon = seg.IndexOf(':');
            if (colon <= 0 || colon >= seg.Length - 1) return false;
            string key = seg.Substring(0, colon);
            string rest = seg.Substring(colon + 1);
            int bar = rest.IndexOf('|');
            string flag = bar < 0 ? rest : rest.Substring(0, bar);
            if (!float.TryParse(flag, NumberStyles.Float, CultureInfo.InvariantCulture, out float raw))
                return false;
            ev.Time = time;
            ev.Key = key;
            ev.Kind = KindOf(key);
            if (ev.Kind == Kind.Pilot)
            {
                ev.On = true;
                ev.State = (int)Math.Round(raw);
                return true;
            }
            ev.On = raw >= 0.5f;
            if (!ev.On || bar < 0) return true;
            var bits = rest.Substring(bar + 1).Split('|');
            if (ev.Kind == Kind.Seat)
            {
                if (bits.Length < 7) return true;
                if (!Read7(bits, 0, ref ev)) return true;
                ev.HasPose = true;
                return true;
            }
            if (bits.Length < 3) return true;
            if (!TryF(bits[0], out ev.X) || !TryF(bits[1], out ev.Y) || !TryF(bits[2], out ev.Z))
                return true;
            ev.HasPose = true;
            if (bits.Length >= 7 && ReadQuat(bits, 3, ref ev))
            {
                if (bits.Length >= 10)
                {
                    TryF(bits[7], out ev.Sx);
                    TryF(bits[8], out ev.Sy);
                    TryF(bits[9], out ev.Sz);
                }
                if (bits.Length >= 13)
                {
                    TryF(bits[10], out ev.Lx);
                    TryF(bits[11], out ev.Ly);
                    TryF(bits[12], out ev.Lz);
                }
                if (bits.Length >= 15)
                {
                    TryF(bits[13], out ev.Wrinkle);
                    TryF(bits[14], out ev.Strength);
                }
            }
            return true;
        }

        static bool Read7(string[] bits, int at, ref Event ev)
        {
            if (!TryF(bits[at], out ev.X) || !TryF(bits[at + 1], out ev.Y) || !TryF(bits[at + 2], out ev.Z))
                return false;
            return ReadQuat(bits, at + 3, ref ev);
        }

        static bool ReadQuat(string[] bits, int at, ref Event ev)
        {
            return TryF(bits[at], out ev.Qx) && TryF(bits[at + 1], out ev.Qy)
                && TryF(bits[at + 2], out ev.Qz) && TryF(bits[at + 3], out ev.Qw);
        }

        public static Kind KindOf(string key)
        {
            if (string.IsNullOrEmpty(key)) return Kind.Canopy;
            if (key[0] == 'p' || key[0] == 'P') return Kind.Pilot;
            if (key[0] == 's' || key[0] == 'S') return Kind.Seat;
            if (key[0] == 'f' || key[0] == 'F') return Kind.Fire;
            if (key[0] == 'r' || key[0] == 'R') return Kind.Rig;
            return Kind.Canopy;
        }

        public static View At(List<Event>? ev, float time, float removedAt)
        {
            var off = new View();
            if (ev == null || ev.Count == 0) return off;
            if (removedAt >= 0f && time >= removedAt - 0.0001f) return off;
            if (time < ev[0].Time) return off;
            int i = LastAtOrBefore(ev, time);
            var cur = ev[i];
            if (cur.Kind == Kind.Pilot)
            {
                off.On = true;
                off.State = cur.State;
                return off;
            }
            if (!cur.On) return off;
            var view = From(cur);
            if (i + 1 < ev.Count)
            {
                var nxt = ev[i + 1];
                float span = nxt.Time - cur.Time;
                if (span > 0.0001f && nxt.On && nxt.HasPose && cur.HasPose && time <= nxt.Time
                    && nxt.Kind == cur.Kind)
                {
                    float u = (time - cur.Time) / span;
                    if (u < 0f) u = 0f;
                    if (u > 1f) u = 1f;
                    view.X = cur.X + (nxt.X - cur.X) * u;
                    view.Y = cur.Y + (nxt.Y - cur.Y) * u;
                    view.Z = cur.Z + (nxt.Z - cur.Z) * u;
                    Slerp(cur.Qx, cur.Qy, cur.Qz, cur.Qw, nxt.Qx, nxt.Qy, nxt.Qz, nxt.Qw, u,
                        out view.Qx, out view.Qy, out view.Qz, out view.Qw);
                    view.Sx = cur.Sx + (nxt.Sx - cur.Sx) * u;
                    view.Sy = cur.Sy + (nxt.Sy - cur.Sy) * u;
                    view.Sz = cur.Sz + (nxt.Sz - cur.Sz) * u;
                    view.Lx = cur.Lx + (nxt.Lx - cur.Lx) * u;
                    view.Ly = cur.Ly + (nxt.Ly - cur.Ly) * u;
                    view.Lz = cur.Lz + (nxt.Lz - cur.Lz) * u;
                    view.Wrinkle = cur.Wrinkle + (nxt.Wrinkle - cur.Wrinkle) * u;
                    view.Strength = cur.Strength + (nxt.Strength - cur.Strength) * u;
                }
            }
            return view;
        }

        /// <summary>
        /// The seat-fire clip plays only when playback walks forward across
        /// the sample. A seek, a reverse step, or a pause does not replay it.
        /// </summary>
        public static bool Crossed(List<Event>? ev, float prev, float now, float dt, bool paused)
        {
            if (paused || ev == null || ev.Count == 0) return false;
            if (!(dt > 0.0001f && dt <= 0.35f)) return false;
            if (now < prev) return false;
            for (int i = 0; i < ev.Count; i++)
            {
                var e = ev[i];
                if (!e.On) continue;
                if (e.Time > prev && e.Time <= now + 0.0001f) return true;
            }
            return false;
        }

        public static void Slerp(
            float ax, float ay, float az, float aw,
            float bx, float by, float bz, float bw,
            float t,
            out float x, out float y, out float z, out float w)
        {
            if (t <= 0f) { x = ax; y = ay; z = az; w = aw; return; }
            if (t >= 1f) { x = bx; y = by; z = bz; w = bw; return; }
            float dot = ax * bx + ay * by + az * bz + aw * bw;
            if (dot < 0f)
            {
                bx = -bx; by = -by; bz = -bz; bw = -bw;
                dot = -dot;
            }
            if (dot > 0.9995f)
            {
                x = ax + (bx - ax) * t;
                y = ay + (by - ay) * t;
                z = az + (bz - az) * t;
                w = aw + (bw - aw) * t;
                float m = (float)Math.Sqrt(x * x + y * y + z * z + w * w);
                if (m > 1e-8f) { x /= m; y /= m; z /= m; w /= m; }
                return;
            }
            if (dot > 1f) dot = 1f;
            float theta = (float)Math.Acos(dot);
            float s = (float)Math.Sin(theta);
            float wa = (float)Math.Sin((1f - t) * theta) / s;
            float wb = (float)Math.Sin(t * theta) / s;
            x = ax * wa + bx * wb;
            y = ay * wa + by * wb;
            z = az * wa + bz * wb;
            w = aw * wa + bw * wb;
        }

        static View From(Event cur)
        {
            return new View
            {
                On = true,
                HasPose = cur.HasPose,
                State = cur.State,
                X = cur.X,
                Y = cur.Y,
                Z = cur.Z,
                Qx = cur.Qx,
                Qy = cur.Qy,
                Qz = cur.Qz,
                Qw = cur.Qw,
                Sx = cur.Sx,
                Sy = cur.Sy,
                Sz = cur.Sz,
                Lx = cur.Lx,
                Ly = cur.Ly,
                Lz = cur.Lz,
                Wrinkle = cur.Wrinkle,
                Strength = cur.Strength
            };
        }

        static int LastAtOrBefore(List<Event> ev, float time)
        {
            int lo = 0;
            int hi = ev.Count - 1;
            int i = 0;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (ev[mid].Time <= time)
                {
                    i = mid;
                    lo = mid + 1;
                }
                else hi = mid - 1;
            }
            return i;
        }

        static bool TryF(string text, out float v)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }
    }
}
