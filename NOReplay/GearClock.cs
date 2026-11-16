namespace NOReplay
{
    /// <summary>
    /// Landing gear is a step on the replay clock.
    /// An airframe that spawns in the air starts retracted. One that spawns on
    /// the ground or on a ship starts extended. That is the same test
    /// Aircraft.OnStartClient uses: radar altitude above spawnOffset.y + 1
    /// retracts, otherwise the gear stays down. After spawn, only a recorded
    /// LandingGear key changes it, and a key never applies before its own time.
    /// </summary>
    public static class GearClock
    {
        /// <summary>No raycast hit. The airframe is not sitting on a surface.</summary>
        public const float NoSurface = -1f;

        public static bool OnSurface(float hitDistance, float spawnOffsetY)
        {
            if (hitDistance < 0f) return false;
            if (spawnOffsetY < 0f) spawnOffsetY = 0f;
            float radarAlt = hitDistance - spawnOffsetY;
            return radarAlt <= spawnOffsetY + 1f;
        }

        /// <summary>
        /// True when the gear is extended. Keys are in time order.
        /// The latest key at or before <paramref name="time"/> wins.
        /// </summary>
        public static bool Extended(bool surfaceSpawn, float time, float[] times, float[] gear)
        {
            int key = ActiveKey(time, times);
            if (key < 0 || gear == null || key >= gear.Length) return surfaceSpawn;
            return gear[key] > 0.5f;
        }

        /// <summary>
        /// Fold amount at a replay time. 0 is extended, 1 is fully retracted.
        /// The live hinge used to step 0.6 per second. The amount here is only
        /// a function of the clock, so a seek lands in the middle of the fold.
        /// </summary>
        public const float FoldPerSecond = 0.6f;

        public static float Fold(bool surfaceSpawn, float time, float[] times, float[] gear)
        {
            float spawn = surfaceSpawn ? 0f : 1f;
            int key = ActiveKey(time, times);
            if (key < 0 || gear == null || key >= gear.Length) return spawn;
            bool down = gear[key] > 0.5f;
            float target = down ? 0f : 1f;
            float from = key == 0 ? spawn : (gear[key - 1] > 0.5f ? 0f : 1f);
            float span = time - times[key];
            if (span < 0f) span = 0f;
            float u = span * FoldPerSecond;
            if (u > 1f) u = 1f;
            return from + (target - from) * u;
        }

        public static int ActiveKey(float time, float[] times)
        {
            int found = -1;
            int n = times == null ? 0 : times.Length;
            for (int i = 0; i < n; i++)
            {
                if (times[i] > time + 0.0001f) break;
                found = i;
            }
            return found;
        }
    }
}
