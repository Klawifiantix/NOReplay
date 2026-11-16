using System;

namespace NOReplay
{
    /// <summary>
    /// A missile launch puff that is next to the camera, and far from the
    /// missile, was emitted on the followed aircraft. The real puff belongs
    /// at the launch point.
    /// </summary>
    public static class LaunchFx
    {
        public const float CameraMeters = 40f;
        public const float LaunchMeters = 30f;

        public static bool Stray(float cameraDist, float launchDist)
        {
            return cameraDist < CameraMeters && launchDist > LaunchMeters;
        }

        public static bool LaunchName(string? name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.IndexOf("launch", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("muzzle", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
