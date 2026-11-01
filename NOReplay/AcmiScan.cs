using System;
using System.Globalization;

namespace NOReplay
{
    /// <summary>
    /// ACMI field scan without the string arrays Split allocates per line.
    /// Numeric text is parsed in place. Stored names are still strings.
    /// </summary>
    public static class AcmiScan
    {
        public ref struct Props
        {
            ReadOnlySpan<char> _s;
            int _i;
            public ReadOnlySpan<char> Key;
            public ReadOnlySpan<char> Value;

            public Props(ReadOnlySpan<char> text)
            {
                _s = text;
                _i = 0;
                Key = default;
                Value = default;
            }

            public bool MoveNext()
            {
                while (_i <= _s.Length)
                {
                    if (_i == _s.Length)
                    {
                        _i = _s.Length + 1;
                        return false;
                    }
                    int rel = _s.Slice(_i).IndexOf(',');
                    int end = rel < 0 ? _s.Length : _i + rel;
                    var part = _s.Slice(_i, end - _i);
                    _i = rel < 0 ? _s.Length + 1 : end + 1;
                    if (part.Length == 0) continue;
                    int eq = part.IndexOf('=');
                    if (eq <= 0) continue;
                    Key = part.Slice(0, eq);
                    Value = part.Slice(eq + 1);
                    return true;
                }
                return false;
            }
        }

        public static bool KeyIs(ReadOnlySpan<char> key, string name)
        {
            return key.Equals(name.AsSpan(), StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryFloat(ReadOnlySpan<char> raw, out float value)
        {
            return TryFloat(raw, out value, true);
        }

        /// <summary>
        /// foldComma matches the old val.Replace(',', '.') paths. Fields that
        /// called TryParse directly stay strict so a comma still fails.
        /// </summary>
        public static bool TryFloat(ReadOnlySpan<char> raw, out float value, bool foldComma)
        {
            value = 0f;
            if (raw.Length == 0) return false;
            if (!foldComma || raw.IndexOf(',') < 0)
                return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            Span<char> buf = stackalloc char[raw.Length];
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                buf[i] = c == ',' ? '.' : c;
            }
            return float.TryParse(buf, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        public static bool TryInt(ReadOnlySpan<char> raw, out int value)
        {
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        public static bool TryByte(ReadOnlySpan<char> raw, out byte value)
        {
            return byte.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        public struct Pose
        {
            public bool Any;
            public bool HasX, HasY, HasZ, HasRoll, HasPitch, HasYaw;
            public float X, Y, Z, Roll, Pitch, Yaw;
        }

        /// <summary>
        /// Same branches as the old string.Split('|') transform reader.
        /// </summary>
        public static bool ParsePose(ReadOnlySpan<char> val, out Pose pose)
        {
            pose = default;
            const int Max = 16;
            Span<float> nums = stackalloc float[Max];
            Span<bool> ok = stackalloc bool[Max];
            int count = 0;
            int i = 0;
            if (val.Length == 0)
            {
                count = 1;
            }
            else
            {
                while (count < Max)
                {
                    int rel = val.Slice(i).IndexOf('|');
                    var bit = rel < 0 ? val.Slice(i) : val.Slice(i, rel);
                    ok[count] = bit.Length > 0 && TryFloat(bit, out nums[count], true);
                    count++;
                    if (rel < 0) break;
                    i += rel + 1;
                }
            }

            bool any = false;
            if (count == 3)
            {
                if (ok[0]) { pose.X = nums[0]; pose.HasX = true; any = true; }
                if (ok[1]) { pose.Y = nums[1]; pose.HasY = true; any = true; }
                if (ok[2]) { pose.Z = nums[2]; pose.HasZ = true; any = true; }
                pose.Any = any;
                return any;
            }

            if (count > 2 && ok[2]) { pose.Y = nums[2]; pose.HasY = true; any = true; }
            if (count == 5)
            {
                if (ok[3]) { pose.X = nums[3]; pose.HasX = true; any = true; }
                if (ok[4]) { pose.Z = nums[4]; pose.HasZ = true; any = true; }
                pose.Any = any;
                return any;
            }
            if (count > 3 && ok[3]) { pose.Roll = nums[3]; pose.HasRoll = true; any = true; }
            if (count > 4 && ok[4]) { pose.Pitch = nums[4]; pose.HasPitch = true; any = true; }
            if (count > 5 && ok[5]) { pose.Yaw = nums[5]; pose.HasYaw = true; any = true; }
            if (count > 6 && ok[6]) { pose.X = nums[6]; pose.HasX = true; any = true; }
            if (count > 7 && ok[7]) { pose.Z = nums[7]; pose.HasZ = true; any = true; }
            pose.Any = any;
            return any;
        }

        public static bool ParseQuat(ReadOnlySpan<char> val, out float x, out float y, out float z, out float w)
        {
            x = y = z = w = 0f;
            int n = 0;
            Span<float> nums = stackalloc float[4];
            int i = 0;
            while (n < 4)
            {
                int rel = val.Slice(i).IndexOf('|');
                var bit = rel < 0 ? val.Slice(i) : val.Slice(i, rel);
                if (bit.Length == 0 || !TryFloat(bit, out nums[n], false)) return false;
                n++;
                if (rel < 0) break;
                i += rel + 1;
            }
            if (n < 4) return false;
            x = nums[0];
            y = nums[1];
            z = nums[2];
            w = nums[3];
            return true;
        }
    }
}
