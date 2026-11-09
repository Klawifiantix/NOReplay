using System;

namespace NOReplay
{
    /// <summary>
    /// Nacelle angles recorded as Tilt=a|b|c. Each value is the rotator part's
    /// local X in degrees. Playback blends them. Absent text means an old file.
    /// </summary>
    public static class TiltText
    {
        public struct Set
        {
            public int Count;
            public float[] Angle;

            public Set(int count, float[] angle)
            {
                Count = count;
                Angle = angle;
            }
        }

        public static bool Read(string? text, out Set set)
        {
            set = new Set(0, Array.Empty<float>());
            if (string.IsNullOrEmpty(text)) return false;
            var bits = text.Split('|');
            var vals = new float[bits.Length];
            int n = 0;
            for (int i = 0; i < bits.Length && n < 8; i++)
            {
                if (bits[i].Length == 0) continue;
                if (!float.TryParse(bits[i], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float v))
                    return false;
                vals[n++] = v;
            }
            if (n == 0) return false;
            if (n != vals.Length)
            {
                var trim = new float[n];
                Array.Copy(vals, trim, n);
                vals = trim;
            }
            set = new Set(n, vals);
            return true;
        }

        public static Set ReadOrEmpty(string? text)
        {
            return Read(text, out var set) ? set : new Set(0, Array.Empty<float>());
        }

        public static Set Lerp(Set a, Set b, float u)
        {
            int n = a.Count < b.Count ? a.Count : b.Count;
            if (n <= 0) return new Set(0, Array.Empty<float>());
            var vals = new float[n];
            for (int i = 0; i < n; i++)
                vals[i] = LerpAngle(a.Angle[i], b.Angle[i], u);
            return new Set(n, vals);
        }

        public static float LerpAngle(float a, float b, float u)
        {
            float d = b - a;
            d %= 360f;
            if (d > 180f) d -= 360f;
            if (d < -180f) d += 360f;
            return a + d * u;
        }
    }
}
