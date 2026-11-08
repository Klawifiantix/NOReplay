using System;

namespace NOReplay
{
    /// <summary>
    /// Fills the gap between a ground unit's last pose and its removal.
    /// Old vehicle recordings sample about once a second and stop when the unit
    /// is disabled, so the last point can sit a short distance before the wreck.
    /// The coast is constant velocity, capped in time and speed. Ships do not use it.
    /// </summary>
    public static class TrackCoast
    {
        /// <summary>Do not invent more than one and a half seconds of motion.</summary>
        public const float MaxSeconds = 1.5f;

        /// <summary>Ground vehicles do not coast faster than this, metres per second.</summary>
        public const float MaxSpeed = 45f;

        public static bool Try(
            float time, float removedAt,
            float t0, float x0, float y0, float z0,
            float t1, float x1, float y1, float z1,
            out float x, out float y, out float z,
            out float vx, out float vy, out float vz)
        {
            x = x1;
            y = y1;
            z = z1;
            vx = 0f;
            vy = 0f;
            vz = 0f;
            if (removedAt < 0f || time <= t1 || time >= removedAt) return false;
            float sampleGap = t1 - t0;
            if (sampleGap < 0.05f || sampleGap > 2.5f) return false;
            float gap = removedAt - t1;
            if (gap <= 0.02f) return false;
            float dx = x1 - x0;
            float dy = y1 - y0;
            float dz = z1 - z0;
            float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            float speed = dist / sampleGap;
            if (speed < 0.5f) return false;
            float scale = 1f;
            if (speed > MaxSpeed)
            {
                scale = MaxSpeed / speed;
                speed = MaxSpeed;
            }
            float limit = gap < MaxSeconds ? gap : MaxSeconds;
            float ahead = time - t1;
            if (ahead > limit) ahead = limit;
            float inv = scale / sampleGap;
            vx = dx * inv;
            vy = dy * inv;
            vz = dz * inv;
            x = x1 + vx * ahead;
            y = y1 + vy * ahead;
            z = z1 + vz * ahead;
            return speed > 0f;
        }

        /// <summary>
        /// A rejected coast must not replace the sampled position. Callers that
        /// take the coast outputs on a false return put the unit at the origin.
        /// </summary>
        public static void Apply(bool accepted, float coastX, float coastY, float coastZ,
            float sampleX, float sampleY, float sampleZ, out float x, out float y, out float z)
        {
            x = accepted ? coastX : sampleX;
            y = accepted ? coastY : sampleY;
            z = accepted ? coastZ : sampleZ;
        }
    }
}
