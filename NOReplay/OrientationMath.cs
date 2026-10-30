using System;

namespace NOReplay
{
    /// <summary>
    /// Shortest-arc orientation blend. No Unity types, so the wrap cases can be tested
    /// without the game assemblies.
    /// </summary>
    public static class OrientationMath
    {
        public static void FromUnityEuler(float xDeg, float yDeg, float zDeg,
            out float x, out float y, out float z, out float w)
        {
            float hx = xDeg * (MathF.PI / 360f);
            float hy = yDeg * (MathF.PI / 360f);
            float hz = zDeg * (MathF.PI / 360f);
            float cx = MathF.Cos(hx), sx = MathF.Sin(hx);
            float cy = MathF.Cos(hy), sy = MathF.Sin(hy);
            float cz = MathF.Cos(hz), sz = MathF.Sin(hz);
            w = cy * cx * cz + sy * sx * sz;
            x = cy * sx * cz + sy * cx * sz;
            y = sy * cx * cz - cy * sx * sz;
            z = cy * cx * sz - sy * sx * cz;
        }

        /// <summary>
        /// Same euler order the ghost applies: Quaternion.Euler(-pitch, yaw, -roll).
        /// </summary>
        public static void FromPlaybackEuler(float pitchDeg, float yawDeg, float rollDeg,
            out float x, out float y, out float z, out float w)
        {
            FromUnityEuler(-pitchDeg, yawDeg, -rollDeg, out x, out y, out z, out w);
        }

        public static void AlignHemisphere(
            float ax, float ay, float az, float aw,
            ref float bx, ref float by, ref float bz, ref float bw)
        {
            float dot = ax * bx + ay * by + az * bz + aw * bw;
            if (dot < 0f)
            {
                bx = -bx;
                by = -by;
                bz = -bz;
                bw = -bw;
            }
        }

        public static void Slerp(
            float ax, float ay, float az, float aw,
            float bx, float by, float bz, float bw,
            float t,
            out float x, out float y, out float z, out float w)
        {
            AlignHemisphere(ax, ay, az, aw, ref bx, ref by, ref bz, ref bw);
            float dot = ax * bx + ay * by + az * bz + aw * bw;
            if (dot > 0.9995f)
            {
                x = ax + (bx - ax) * t;
                y = ay + (by - ay) * t;
                z = az + (bz - az) * t;
                w = aw + (bw - aw) * t;
                Normalize(ref x, ref y, ref z, ref w);
                return;
            }
            if (dot < -1f) dot = -1f;
            else if (dot > 1f) dot = 1f;
            float theta = MathF.Acos(dot);
            float sin = MathF.Sin(theta);
            if (sin < 1e-6f)
            {
                x = ax; y = ay; z = az; w = aw;
                Normalize(ref x, ref y, ref z, ref w);
                return;
            }
            float wa = MathF.Sin((1f - t) * theta) / sin;
            float wb = MathF.Sin(t * theta) / sin;
            x = ax * wa + bx * wb;
            y = ay * wa + by * wb;
            z = az * wa + bz * wb;
            w = aw * wa + bw * wb;
            Normalize(ref x, ref y, ref z, ref w);
        }

        public static void Normalize(ref float x, ref float y, ref float z, ref float w)
        {
            float len = MathF.Sqrt(x * x + y * y + z * z + w * w);
            if (len < 1e-8f)
            {
                x = 0f; y = 0f; z = 0f; w = 1f;
                return;
            }
            float inv = 1f / len;
            x *= inv; y *= inv; z *= inv; w *= inv;
        }
    }
}
