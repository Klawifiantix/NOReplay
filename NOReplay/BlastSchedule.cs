using System;

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

        /// <summary>
        /// A Boom written on the wreck line shares the removal time.
        /// SpawnWreckage does not detonate anything, so that mark is not a blast.
        /// </summary>
        public static bool OnRemoval(float time, float removed)
        {
            return time >= 0f && removed >= 0f && Math.Abs(time - removed) <= SameHitSeconds;
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
