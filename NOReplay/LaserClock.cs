using System;
using System.Collections.Generic;
using System.Globalization;

namespace NOReplay
{
    /// <summary>
    /// Beam on/off, impact point and power for one emitter. The state is a
    /// function of replay time. An empty channel stays off, so an old file
    /// does not invent a shot.
    /// </summary>
    public static class LaserClock
    {
        public struct Event
        {
            public float Time;
            public string Key;
            public bool On;
            public bool HasPoint;
            public float X, Y, Z, Power;
        }

        public struct View
        {
            public bool On;
            public bool HasPoint;
            public float X, Y, Z, Power;
            public float Age;
        }

        public struct Edge
        {
            public bool PlayStart;
            public bool PlayEnd;
            public bool Loop;
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
            if (key.Length == 0) return false;
            string rest = seg.Substring(colon + 1);
            int bar = rest.IndexOf('|');
            string flag = bar < 0 ? rest : rest.Substring(0, bar);
            if (!float.TryParse(flag, NumberStyles.Float, CultureInfo.InvariantCulture, out float onRaw))
                return false;
            ev.Time = time;
            ev.Key = key;
            ev.On = onRaw >= 0.5f;
            if (!ev.On || bar < 0)
            {
                ev.Power = ev.On ? 1f : 0f;
                return true;
            }
            var bits = rest.Substring(bar + 1).Split('|');
            if (bits.Length < 3) return true;
            if (!TryF(bits[0], out ev.X) || !TryF(bits[1], out ev.Y) || !TryF(bits[2], out ev.Z))
                return true;
            ev.HasPoint = true;
            ev.Power = 1f;
            if (bits.Length >= 4 && TryF(bits[3], out float p))
            {
                if (p < 0f) p = 0f;
                if (p > 1f) p = 1f;
                ev.Power = p;
            }
            return true;
        }

        static bool TryF(string text, out float v)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        public static View At(List<Event>? ev, float time, float removedAt)
        {
            var off = new View();
            if (ev == null || ev.Count == 0) return off;
            if (removedAt >= 0f && time >= removedAt - 0.0001f) return off;
            int n = ev.Count;
            if (time < ev[0].Time) return off;
            int i = 0;
            int lo = 0;
            int hi = n - 1;
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
            var cur = ev[i];
            if (!cur.On) return off;
            var view = new View
            {
                On = true,
                HasPoint = cur.HasPoint,
                X = cur.X,
                Y = cur.Y,
                Z = cur.Z,
                Power = cur.Power,
                Age = 0f
            };
            if (i + 1 < n)
            {
                var nxt = ev[i + 1];
                float span = nxt.Time - cur.Time;
                if (span > 0.0001f && cur.HasPoint && nxt.On && nxt.HasPoint && time <= nxt.Time)
                {
                    float u = (time - cur.Time) / span;
                    if (u < 0f) u = 0f;
                    if (u > 1f) u = 1f;
                    view.X = cur.X + (nxt.X - cur.X) * u;
                    view.Y = cur.Y + (nxt.Y - cur.Y) * u;
                    view.Z = cur.Z + (nxt.Z - cur.Z) * u;
                    view.Power = cur.Power + (nxt.Power - cur.Power) * u;
                    view.HasPoint = true;
                }
            }
            int s = i;
            while (s > 0 && ev[s - 1].On) s--;
            view.Age = time - ev[s].Time;
            if (view.Age < 0f) view.Age = 0f;
            return view;
        }

        /// <summary>
        /// Start and end clicks only on a short forward step across the edge.
        /// A jump, a reverse step, or a pause does not invent those clicks.
        /// The loop follows the burst age.
        /// </summary>
        public static Edge Audio(View now, bool hadPrev, View prev, float dt, bool paused)
        {
            if (paused) return default;
            bool forward = hadPrev && dt > 0.0001f && dt <= 0.35f;
            if (!now.On)
            {
                if (forward && prev.On) return new Edge { PlayEnd = true };
                return default;
            }
            return new Edge
            {
                PlayStart = forward && !prev.On,
                Loop = now.Age > 0.2f
            };
        }

        public static float SustainPitch(bool modify, float pitch, float startMultiplier, float fallRate, float age)
        {
            if (!modify) return pitch;
            float start = pitch * startMultiplier;
            float fallen = age > 0.2f ? fallRate * (age - 0.2f) : 0f;
            float v = start - fallen;
            return v < pitch ? pitch : v;
        }
    }
}
