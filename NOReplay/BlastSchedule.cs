namespace NOReplay
{
    /// <summary>
    /// Ammunition blasts are a list of replay times, not one latch.
    /// Two marks closer than a twentieth of a second are the same hit.
    /// </summary>
    public static class BlastSchedule
    {
        public const float SameHitSeconds = 0.05f;

        public static bool IsLater(float time, float previous)
        {
            return previous < 0f || time > previous + SameHitSeconds;
        }

        public static int Due(float now, float[] times)
        {
            int n = times == null ? 0 : times.Length;
            int due = 0;
            for (int i = 0; i < n; i++)
            {
                if (now + 0.0001f < times[i]) break;
                due++;
            }
            return due;
        }
    }
}
