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

        /// <summary>The wreck model is due once the replay clock reaches Visible=0.</summary>
        public static bool RemovalDue(float now, float removed)
        {
            return removed >= 0f && now + 0.0001f >= removed;
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

    /// <summary>
    /// DamageParticles.SlowUpdate fades new particles after the system duration.
    /// The first tick is one second after the effect starts, then once a second.
    /// fadeDuration of zero does not fade.
    /// </summary>
    public static class FireFade
    {
        public static float Opacity(float elapsed, float gate, float fadeDuration)
        {
            if (fadeDuration <= 0f || elapsed < 1f) return 1f;
            int time = (int)elapsed;
            int steps = time - (int)Math.Floor(gate);
            if (steps <= 0) return 1f;
            float opacity = 1f - steps / Math.Max(fadeDuration, 0.5f);
            return opacity > 0f ? opacity : 0f;
        }

        public static float ClearAt(float gate, float fadeDuration, float life)
        {
            if (fadeDuration <= 0f) return life;
            float step = 1f / Math.Max(fadeDuration, 0.5f);
            int need = (int)Math.Ceiling(1f / step);
            return (int)Math.Floor(gate) + need + life;
        }
    }
}
