using System;
using System.Collections.Generic;
using System.Globalization;

namespace NOReplay
{
    /// <summary>
    /// Kill feed, chat and mission lines on the replay clock. An empty list
    /// stays blank, so an older file shows nothing.
    /// </summary>
    public static class FeedLog
    {
        public enum Kind : byte
        {
            Kill = 0,
            Global = 1,
            Ally = 2,
            Server = 3,
            Mission = 4
        }

        public struct Item
        {
            public float Time;
            public Kind Kind;
            public int KillType;
            public float Value;
            public int Flags;
            public int KR, KG, KB, VR, VG, VB;
            public bool Sound;
            public string A;
            public string B;
        }

        public const float KillHold = 5f;
        public const float MessageHold = 8f;
        public const float HoldPerChar = 0.1f;

        public static string Escape(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new System.Text.StringBuilder(text.Length + 4);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\\') sb.Append("\\\\");
                else if (c == '|') sb.Append("\\p");
                else if (c == ',') sb.Append("\\c");
                else if (c == '\n' || c == '\r') sb.Append("\\n");
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static string Unescape(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new System.Text.StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\\' && i + 1 < text.Length)
                {
                    char n = text[++i];
                    if (n == '\\') sb.Append('\\');
                    else if (n == 'p') sb.Append('|');
                    else if (n == 'c') sb.Append(',');
                    else if (n == 'n') sb.Append('\n');
                    else sb.Append(n);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static bool TryParse(string? text, float time, out Item item)
        {
            item = default;
            if (string.IsNullOrEmpty(text)) return false;
            int bar = text.IndexOf('|');
            if (bar <= 0) return false;
            string head = text.Substring(0, bar);
            string rest = text.Substring(bar + 1);
            item.Time = time;
            item.A = "";
            item.B = "";
            if (head == "K") return ParseKill(rest, ref item);
            if (head == "G") { item.Kind = Kind.Global; return ParseChat(rest, ref item); }
            if (head == "A") { item.Kind = Kind.Ally; return ParseChat(rest, ref item); }
            if (head == "S") { item.Kind = Kind.Server; item.B = Unescape(rest); return item.B.Length > 0; }
            if (head == "M") return ParseMission(rest, ref item);
            return false;
        }

        static bool ParseKill(string rest, ref Item item)
        {
            var p = Split(rest);
            if (p.Count < 11) return false;
            item.Kind = Kind.Kill;
            if (!int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out item.KillType)) return false;
            if (!float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out item.Value)) return false;
            if (!int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out item.Flags)) return false;
            if (!ByteOf(p[3], out item.KR) || !ByteOf(p[4], out item.KG) || !ByteOf(p[5], out item.KB)) return false;
            if (!ByteOf(p[6], out item.VR) || !ByteOf(p[7], out item.VG) || !ByteOf(p[8], out item.VB)) return false;
            item.A = Unescape(p[9]);
            item.B = Unescape(JoinRaw(p, 10));
            return item.B.Length > 0;
        }

        static bool ParseChat(string rest, ref Item item)
        {
            var p = Split(rest);
            if (p.Count < 5) return false;
            if (!ByteOf(p[0], out item.KR) || !ByteOf(p[1], out item.KG) || !ByteOf(p[2], out item.KB)) return false;
            item.A = Unescape(p[3]);
            item.B = Unescape(JoinRaw(p, 4));
            return item.B.Length > 0 || item.A.Length > 0;
        }

        static bool ParseMission(string rest, ref Item item)
        {
            int bar = rest.IndexOf('|');
            if (bar <= 0) return false;
            item.Kind = Kind.Mission;
            item.Sound = rest.Substring(0, bar) == "1";
            item.B = Unescape(rest.Substring(bar + 1));
            return item.B.Length > 0;
        }

        public static string FormatKill(Item item)
        {
            string victim = Paint(item.B, item.VR, item.VG, item.VB);
            string verb = Verb(item.KillType, item.A.Length > 0);
            if (item.A.Length == 0) return victim + " " + verb;
            return Paint(item.A, item.KR, item.KG, item.KB) + " " + verb + " " + victim;
        }

        public static string FormatChat(Item item)
        {
            if (item.Kind == Kind.Server) return item.B ?? "";
            string who = (item.Kind == Kind.Ally ? "(ally)" : "") + (item.A ?? "") + ":";
            return Paint(who, item.KR, item.KG, item.KB) + " " + (item.B ?? "");
        }

        public static string Verb(int killType, bool hasKiller)
        {
            if (hasKiller)
            {
                switch (killType)
                {
                    case 0: return "shot down";
                    case 1: return "destroyed";
                    case 2: return "demolished";
                    case 3: return "intercepted";
                    case 4: return "sank";
                }
            }
            else
            {
                switch (killType)
                {
                    case 0: return "crashed";
                    case 1: return "was destroyed";
                    case 2: return "collapsed";
                    case 3: return "";
                    case 4: return "sank";
                }
            }
            return "";
        }

        public static bool PassesKill(Item item, int filter, float minValue)
        {
            if (item.Kind != Kind.Kill) return false;
            if (item.Value < minValue) return false;
            if (filter == 0) return false;
            if (filter == 4) return true;
            bool killerLocal = (item.Flags & 1) != 0;
            bool victimLocal = (item.Flags & 2) != 0;
            bool killerFriend = (item.Flags & 4) != 0;
            bool victimFriend = (item.Flags & 8) != 0;
            bool killerEnemy = (item.Flags & 16) != 0;
            bool victimEnemy = (item.Flags & 32) != 0;
            if (filter == 1) return killerLocal || victimLocal;
            if (filter == 2) return killerFriend || victimFriend;
            if (filter == 3) return killerEnemy || victimEnemy;
            return true;
        }

        public static bool Visible(float eventTime, float now, Kind kind, int chars)
        {
            if (now + 0.0001f < eventTime) return false;
            float hold = (kind == Kind.Kill ? KillHold : MessageHold) + HoldPerChar * chars;
            return now < eventTime + hold;
        }

        public static bool Crossed(float prev, float now, float eventTime, float dt, bool paused)
        {
            if (paused) return false;
            if (!(dt > 0.0001f && dt <= 0.5f)) return false;
            return eventTime > prev && eventTime <= now + 0.0001f;
        }

        public static string Paint(string? text, int r, int g, int b)
        {
            if (r < 0) r = 0;
            if (g < 0) g = 0;
            if (b < 0) b = 0;
            if (r > 255) r = 255;
            if (g > 255) g = 255;
            if (b > 255) b = 255;
            return "<color=#" + r.ToString("X2") + g.ToString("X2") + b.ToString("X2") + "FF>" + (text ?? "") + "</color>";
        }

        static bool ByteOf(string text, out int v)
        {
            v = 0;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return false;
            if (v < 0) v = 0;
            if (v > 255) v = 255;
            return true;
        }

        static List<string> Split(string text)
        {
            var list = new List<string>();
            int i = 0;
            while (i <= text.Length)
            {
                int rel = i < text.Length ? text.IndexOf('|', i) : -1;
                int end = rel < 0 ? text.Length : rel;
                list.Add(text.Substring(i, end - i));
                if (rel < 0) break;
                i = rel + 1;
            }
            return list;
        }

        static string JoinRaw(List<string> parts, int start)
        {
            if (start >= parts.Count) return "";
            if (start == parts.Count - 1) return parts[start];
            var sb = new System.Text.StringBuilder();
            for (int i = start; i < parts.Count; i++)
            {
                if (i > start) sb.Append('|');
                sb.Append(parts[i]);
            }
            return sb.ToString();
        }
    }
}
