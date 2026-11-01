using System;

namespace NOReplay
{
    /// <summary>
    /// Rolling distance for a ground-vehicle wheel or track.
    /// Mirrors GroundVehicle.AnimateWheels: signed meters along the nose,
    /// and a skid-steer term so a pivot turns the two sides opposite ways.
    /// No Unity types, so the sign can be tested without the game assemblies.
    /// </summary>
    public static class GroundRoll
    {
        public const float MaxLever = 20f;
        public const float TrackScrollPerMeter = -0.2f;
        public const float RadToDeg = 57.29578f;
        public const float DefaultRadius = 0.3f;

        /// <summary>
        /// Meters the contact patch traveled forward.
        /// forwardDisp is meters along the nose (negative when reversing).
        /// yawRad is positive when the nose yaws right.
        /// localX is meters to the right of the centerline.
        /// </summary>
        public static float Travel(float forwardDisp, float yawRad, float localX)
        {
            if (float.IsNaN(forwardDisp) || float.IsNaN(yawRad)) return 0f;
            if (localX > MaxLever || localX < -MaxLever || float.IsNaN(localX)) localX = 0f;
            return forwardDisp - yawRad * localX;
        }

        public static float WheelDegrees(float travel, float radius)
        {
            if (radius < 0.02f || float.IsNaN(radius)) radius = DefaultRadius;
            return travel * RadToDeg / radius;
        }

        public static float TrackOffset(float travel)
        {
            return travel * TrackScrollPerMeter;
        }

        /// <summary>
        /// A timeline jump. Normal playback and a slow scrub stay under these limits.
        /// </summary>
        public static bool IsSeek(float clockDt, float dispSqr, float yawDeg)
        {
            if (clockDt > 1f || clockDt < -1f) return true;
            if (dispSqr > 400f) return true;
            if (yawDeg > 35f || yawDeg < -35f) return true;
            return false;
        }
    }
}
