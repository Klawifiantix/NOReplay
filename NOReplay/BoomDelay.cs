using System;

namespace NOReplay
{
    /// <summary>
    /// Same arrival rule as ExplosionAudioManager: the wave starts at
    /// Pow(yield, 0.3333) * 0.5 and expands at 340 m/s. The listener hears it
    /// once the current distance is inside that front. ExplosionAudio.Start
    /// only registers a layer when the camera is inside the source maxDistance
    /// at the detonation.
    /// </summary>
    internal static class BoomDelay
    {
        public const float SoundSpeed = 340f;

        public static float Front(float yield)
        {
            if (yield <= 0f) return 0f;
            return MathF.Pow(yield, 0.3333f) * 0.5f;
        }

        public static float Seconds(float distance, float yield)
        {
            float dist = distance > 0f ? distance : 0f;
            float front = Front(yield);
            if (dist < front) return 0f;
            return (dist - front) / SoundSpeed;
        }

        public static bool InRangeAtDetonation(float distance, float maxDistance)
        {
            if (maxDistance <= 0f) return false;
            return distance < maxDistance;
        }

        public static bool Crosses(float born, float delay, float prev, float now, bool paused, bool reverse, bool seek)
        {
            return Reached(born, delay, delay, prev, now, paused, reverse, seek);
        }

        /// <summary>
        /// True when the wave did not contain the camera at <paramref name="prev"/>
        /// and does at <paramref name="now"/>. A camera that closes the distance
        /// can pull the arrival earlier than the previous frame.
        /// </summary>
        public static bool Reached(float born, float previousDelay, float delay, float prev, float now, bool paused, bool reverse, bool seek)
        {
            if (paused || reverse || seek) return false;
            if (!(now > prev)) return false;
            bool wasInside = prev >= born + previousDelay;
            bool inside = now >= born + delay;
            return inside && !wasInside;
        }
    }
}
