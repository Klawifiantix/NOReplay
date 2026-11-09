using System;
using System.Collections.Generic;
using System.Globalization;

namespace NOReplay
{
    /// <summary>
    /// Cargo ramp angle, simple bay-door angle, and the mounted cargo's
    /// position on the rail. An empty channel leaves the prefab alone, so an
    /// old file does not open a ramp or hide a load.
    /// </summary>
    public static class DoorClock
    {
        public struct Event
        {
            public float Time;
            public string Key;
            public bool Mount;
            public bool On;
            public bool HasPose;
            public byte Mode;
            public float Angle;
            public float X, Y, Z;
        }

        public struct View
        {
            public bool Present;
            public bool Mount;
            public bool On;
            public bool HasPose;
            public byte Mode;
            public float Angle;
            public float X, Y, Z;
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
            ev.Time = time;
            ev.Key = seg.Substring(0, colon);
            ev.Mount = ev.Key.Length > 0 && (ev.Key[0] == 'm' || ev.Key[0] == 'M');
            string rest = seg.Substring(colon + 1);
            if (ev.Mount)
            {
                int bar = rest.IndexOf('|');
                string flag = bar < 0 ? rest : rest.Substring(0, bar);
                if (!float.TryParse(flag, NumberStyles.Float, CultureInfo.InvariantCulture, out float on))
                    return false;
                ev.On = on >= 0.5f;
                if (!ev.On || bar < 0) return true;
                var bits = rest.Substring(bar + 1).Split('|');
                if (bits.Length < 3) return true;
                if (!TryF(bits[0], out ev.X) || !TryF(bits[1], out ev.Y) || !TryF(bits[2], out ev.Z))
                    return true;
                ev.HasPose = true;
                return true;
            }
            if (rest.Length > 2 && rest[1] == '|' && (rest[0] == 'c' || rest[0] == 'C'))
            {
                var bits = rest.Substring(2).Split('|');
                if (bits.Length < 2 || !TryF(bits[0], out float cur) || !TryF(bits[1], out float off))
                    return false;
                ev.On = true;
                ev.HasPose = true;
                ev.Mode = 1;
                ev.Angle = cur + off;
                return true;
            }
            if (rest.Length > 2 && rest[1] == '|' && (rest[0] == 'o' || rest[0] == 'O'))
            {
                if (!TryF(rest.Substring(2), out ev.Angle)) return false;
                ev.On = true;
                ev.HasPose = true;
                ev.Mode = 2;
                return true;
            }
            if (!TryF(rest, out ev.Angle)) return false;
            ev.On = true;
            ev.HasPose = true;
            ev.Mode = 0;
            return true;
        }

        public static View At(List<Event>? ev, float time, float removedAt)
        {
            var none = new View();
            if (ev == null || ev.Count == 0) return none;
            if (removedAt >= 0f && time >= removedAt - 0.0001f)
            {
                if (ev[0].Mount) return new View { Present = true, Mount = true, On = false };
                return new View { Present = true, On = true, HasPose = true, Mode = ev[ev.Count - 1].Mode, Angle = ev[ev.Count - 1].Angle };
            }
            if (time < ev[0].Time)
            {
                if (ev[0].Mount) return new View { Present = true, Mount = true, On = true };
                return new View { Present = true, On = true, HasPose = true, Angle = 0f };
            }
            int i = 0;
            int lo = 0;
            int hi = ev.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (ev[mid].Time <= time) { i = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            var cur = ev[i];
            if (cur.Mount && !cur.On) return new View { Present = true, Mount = true, On = false };
            var view = new View
            {
                Present = true,
                Mount = cur.Mount,
                On = true,
                HasPose = cur.HasPose,
                Mode = cur.Mode,
                Angle = cur.Angle,
                X = cur.X,
                Y = cur.Y,
                Z = cur.Z
            };
            if (i + 1 < ev.Count)
            {
                var nxt = ev[i + 1];
                float span = nxt.Time - cur.Time;
                if (span > 0.0001f && time <= nxt.Time && nxt.On && cur.HasPose && nxt.HasPose && nxt.Mode == cur.Mode)
                {
                    float u = (time - cur.Time) / span;
                    if (u < 0f) u = 0f;
                    if (u > 1f) u = 1f;
                    if (cur.Mount)
                    {
                        view.X = cur.X + (nxt.X - cur.X) * u;
                        view.Y = cur.Y + (nxt.Y - cur.Y) * u;
                        view.Z = cur.Z + (nxt.Z - cur.Z) * u;
                    }
                    else
                        view.Angle = cur.Angle + (nxt.Angle - cur.Angle) * u;
                }
            }
            return view;
        }

        static bool TryF(string text, out float v)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }
    }
}
