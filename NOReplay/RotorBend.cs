using System;
using System.Globalization;

namespace NOReplay
{
    /// <summary>
    /// The blade bend SwashRotor writes. flapAngle is radians on each blade.
    /// The span hinge is local X = -flapAngle in degrees. Segments droop only
    /// while the shaft ratio is below 0.2 and the flap is negative, which is
    /// BendSegments. The ratio is angularSpeed / angularSpeedLimit, not the
    /// averaged RpmRatio key.
    /// </summary>
    public static class RotorBend
    {
        public const float RadToDeg = 57.29578f;
        public const float DroopRatio = 0.2f;
        public const float FlapThreshold = 0.02f;
        public const float RatioThreshold = 0.02f;
        /// <summary>Fastest a bend line is written. Twenty hertz.</summary>
        public const float MinInterval = 0.05f;

        public struct Frame
        {
            public float[] Ratio;
            public float[][] Flap;
        }

        public static float SpanXDeg(float flapRad)
        {
            return (0f - flapRad) * RadToDeg;
        }

        public static bool Droops(float flapRad, float ratio)
        {
            return ratio < DroopRatio && flapRad < 0f;
        }

        /// <summary>
        /// Segment local X in degrees. Index 0 is the hinge piece.
        /// The divisor is the full segment count, matching BendSegments.
        /// </summary>
        public static float SegmentXDeg(float flapRad, int segmentCount, int index)
        {
            float num2 = (0f - flapRad) * RadToDeg;
            if (index <= 0) return (0f - num2) * 0.5f;
            int denom = segmentCount - 1;
            if (denom < 1) denom = 1;
            return num2 / denom;
        }

        public static bool Changed(Frame prev, Frame next)
        {
            if (prev.Ratio == null || next.Ratio == null) return true;
            if (prev.Ratio.Length != next.Ratio.Length) return true;
            for (int s = 0; s < next.Ratio.Length; s++)
            {
                if (Math.Abs(next.Ratio[s] - prev.Ratio[s]) >= RatioThreshold) return true;
                bool was = DroopAny(prev, s);
                bool now = DroopAny(next, s);
                if (was != now) return true;
                var a = prev.Flap != null && s < prev.Flap.Length ? prev.Flap[s] : null;
                var b = next.Flap != null && s < next.Flap.Length ? next.Flap[s] : null;
                int na = a == null ? 0 : a.Length;
                int nb = b == null ? 0 : b.Length;
                if (na != nb) return true;
                for (int i = 0; i < nb; i++)
                    if (Math.Abs(b[i] - a[i]) >= FlapThreshold) return true;
            }
            return false;
        }

        static bool DroopAny(Frame frame, int shaft)
        {
            if (frame.Flap == null || shaft >= frame.Flap.Length || frame.Flap[shaft] == null) return false;
            float ratio = frame.Ratio[shaft];
            var flaps = frame.Flap[shaft];
            for (int i = 0; i < flaps.Length; i++)
                if (Droops(flaps[i], ratio)) return true;
            return false;
        }

        public static string Write(Frame frame)
        {
            var inv = CultureInfo.InvariantCulture;
            var text = new System.Text.StringBuilder(64);
            int shafts = frame.Ratio == null ? 0 : frame.Ratio.Length;
            for (int s = 0; s < shafts; s++)
            {
                if (s > 0) text.Append('|');
                text.Append(frame.Ratio[s].ToString("0.000", inv));
                var flaps = frame.Flap != null && s < frame.Flap.Length ? frame.Flap[s] : null;
                int n = flaps == null ? 0 : flaps.Length;
                for (int i = 0; i < n; i++)
                {
                    text.Append('/');
                    text.Append(flaps[i].ToString("0.000", inv));
                }
            }
            return text.ToString();
        }

        public static bool Read(ReadOnlySpan<char> text, out Frame frame)
        {
            frame = default;
            if (text.Length == 0) return false;
            int shafts = 1;
            for (int i = 0; i < text.Length; i++)
                if (text[i] == '|') shafts++;
            var ratio = new float[shafts];
            var flap = new float[shafts][];
            int s = 0;
            int start = 0;
            for (int i = 0; i <= text.Length && s < shafts; i++)
            {
                if (i != text.Length && text[i] != '|') continue;
                if (!ReadShaft(text.Slice(start, i - start), out ratio[s], out flap[s])) return false;
                s++;
                start = i + 1;
            }
            if (s != shafts) return false;
            frame.Ratio = ratio;
            frame.Flap = flap;
            return true;
        }

        static bool ReadShaft(ReadOnlySpan<char> text, out float ratio, out float[] flap)
        {
            ratio = 0f;
            flap = Array.Empty<float>();
            int parts = 1;
            for (int i = 0; i < text.Length; i++)
                if (text[i] == '/') parts++;
            if (parts < 1) return false;
            var nums = new float[parts];
            int n = 0;
            int start = 0;
            for (int i = 0; i <= text.Length && n < parts; i++)
            {
                if (i != text.Length && text[i] != '/') continue;
                if (!float.TryParse(text.Slice(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out nums[n]))
                    return false;
                n++;
                start = i + 1;
            }
            if (n < 1) return false;
            ratio = nums[0];
            flap = new float[n - 1];
            for (int i = 1; i < n; i++) flap[i - 1] = nums[i];
            return true;
        }

        public static Frame Lerp(Frame a, Frame b, float u)
        {
            if (a.Ratio == null) return b;
            if (b.Ratio == null || a.Ratio.Length != b.Ratio.Length) return a;
            if (u <= 0f) return a;
            if (u >= 1f) return b;
            var frame = new Frame
            {
                Ratio = new float[a.Ratio.Length],
                Flap = new float[a.Ratio.Length][]
            };
            for (int s = 0; s < a.Ratio.Length; s++)
            {
                frame.Ratio[s] = a.Ratio[s] + (b.Ratio[s] - a.Ratio[s]) * u;
                var fa = a.Flap != null && s < a.Flap.Length ? a.Flap[s] : null;
                var fb = b.Flap != null && s < b.Flap.Length ? b.Flap[s] : null;
                int na = fa == null ? 0 : fa.Length;
                int nb = fb == null ? 0 : fb.Length;
                if (na != nb)
                {
                    frame.Flap[s] = fa ?? Array.Empty<float>();
                    continue;
                }
                var flap = new float[na];
                for (int i = 0; i < na; i++)
                    flap[i] = fa[i] + (fb[i] - fa[i]) * u;
                frame.Flap[s] = flap;
            }
            return frame;
        }
    }
}
