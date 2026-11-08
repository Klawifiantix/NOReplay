using System;

namespace NOReplay
{
    /// <summary>
    /// Seeded ballistic path for one wreck fragment. The same age always
    /// returns the same place, so a scrub puts the piece back.
    /// </summary>
    public static class WreckThrow
    {
        public const float Gravity = 9.81f;

        public struct Throw
        {
            public float Vx, Vy, Vz;
            public float Spin;
            public float Ax, Ay, Az;
        }

        public static Throw Seed(int seed, int index, float speed)
        {
            uint h = (uint)seed * 73856093u ^ (uint)(index + 1) * 19349663u;
            float x = HashSigned(ref h);
            float y = HashSigned(ref h);
            float z = HashSigned(ref h);
            float mag = (float)Math.Sqrt(x * x + y * y + z * z);
            if (mag < 0.001f) mag = 1f;
            x /= mag;
            y /= mag;
            z /= mag;
            if (y < 0.15f) y = 0.15f;
            float spin = HashSigned(ref h) * 2.5f;
            float ax = HashSigned(ref h);
            float ay = HashSigned(ref h);
            float az = HashSigned(ref h);
            float am = (float)Math.Sqrt(ax * ax + ay * ay + az * az);
            if (am < 0.001f) am = 1f;
            return new Throw
            {
                Vx = x * speed,
                Vy = y * speed,
                Vz = z * speed,
                Spin = spin,
                Ax = ax / am,
                Ay = ay / am,
                Az = az / am
            };
        }

        public static float ImpactTime(float y0, float vy, float groundY)
        {
            float a = -0.5f * Gravity;
            float b = vy;
            float c = y0 - groundY;
            float disc = b * b - 4f * a * c;
            if (disc < 0f) return float.PositiveInfinity;
            float s = (float)Math.Sqrt(disc);
            float t1 = (-b - s) / (2f * a);
            float t2 = (-b + s) / (2f * a);
            float t = float.PositiveInfinity;
            if (t1 > 0.02f) t = t1;
            if (t2 > 0.02f && t2 < t) t = t2;
            return t;
        }

        public static void Place(float age, float x0, float y0, float z0, Throw throw_, float groundY,
            out float x, out float y, out float z)
        {
            if (age < 0f) age = 0f;
            float hit = ImpactTime(y0, throw_.Vy, groundY);
            float t = age < hit ? age : hit;
            x = x0 + throw_.Vx * t;
            z = z0 + throw_.Vz * t;
            y = y0 + throw_.Vy * t - 0.5f * Gravity * t * t;
            if (y < groundY) y = groundY;
        }

        static float HashSigned(ref uint h)
        {
            h = h * 1664525u + 1013904223u;
            return (h & 65535) / 32767.5f - 1f;
        }
    }
}
