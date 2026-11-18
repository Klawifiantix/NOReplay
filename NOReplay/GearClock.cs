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
        /// A carrier deck is a few metres under the aircraft. A ray that only
        /// finds the terrain far below that deck is not an air spawn yet.
        /// </summary>
        public const float CloseRayMeters = 40f;
        public const int SpawnTries = 45;
        /// <summary>Radar altitude above this is an air spawn. The gear starts retracted.</summary>
        public const float AirRadarMeters = 2f;

        /// <summary>No deck found is a negative gap. Only a real deck, from touching it up to 20 m above, counts.</summary>
        public static bool OnDeck(float gap)
        {
            return gap >= 0f && gap <= 20f;
        }

        /// <summary>
        /// Gear down at spawn: sitting within 2 m radar altitude, or above a ship deck.
        /// A far terrain hit with no deck is airborne.
        /// </summary>
        public static bool OnGround(float rayDistance, float spawnOffsetY, float deckGap)
        {
            if (OnDeck(deckGap)) return true;
            if (rayDistance < 0f) return false;
            if (spawnOffsetY < 0f) spawnOffsetY = 0f;
            float radarAlt = rayDistance - spawnOffsetY;
            return radarAlt <= AirRadarMeters;
        }

        public static bool SpawnReady(int tries, float rayDistance, bool deckNear)
        {
            if (deckNear) return true;
            if (rayDistance >= 0f && rayDistance <= CloseRayMeters) return true;
            return tries >= SpawnTries;
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
