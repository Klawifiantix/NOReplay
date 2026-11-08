namespace NOReplay
{
    /// <summary>
    /// A detached gear hinge can pick up a huge non-uniform scale when it is
    /// unparented. Ordinary strut scales are uneven, but not by this much.
    /// </summary>
    public static class GearScale
    {
        public const float MaxAbs = 8f;
        public const float MaxRatio = 25f;

        public static bool Absurd(float x, float y, float z)
        {
            float ax = x < 0f ? -x : x;
            float ay = y < 0f ? -y : y;
            float az = z < 0f ? -z : z;
            float max = ax > ay ? ax : ay;
            if (az > max) max = az;
            float min = ax < ay ? ax : ay;
            if (az < min) min = az;
            if (max > MaxAbs) return true;
            if (min < 0.0001f) return max > 1f;
            return max / min > MaxRatio;
        }
    }
}
