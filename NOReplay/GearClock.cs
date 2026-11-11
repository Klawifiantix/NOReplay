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
