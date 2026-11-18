using System;

namespace NOReplay
{
    /// <summary>
    /// Where a fragment ends up when the wreck root is moved.
    /// Unparenting keeps the fragment's world position. Parenting keeps it
    /// on the root, so the fragment moves by the same offset as the root.
    /// </summary>
    public static class WreckPose
    {
        public static void Fragment(float fx, float fy, float fz, float rootX0, float rootY0, float rootZ0,
            float rootX1, float rootY1, float rootZ1, bool stillParented,
            out float x, out float y, out float z)
        {
            if (!stillParented)
            {
                x = fx;
                y = fy;
                z = fz;
                return;
            }
            x = fx + (rootX1 - rootX0);
            y = fy + (rootY1 - rootY0);
            z = fz + (rootZ1 - rootZ0);
        }

        public static bool FlamesStay(bool awakeRan, bool stopWhenParentCulled)
        {
            return awakeRan && !stopWhenParentCulled;
        }

        public static bool CollapseDust(int dustEntries)
        {
            return dustEntries > 0;
        }

        /// <summary>
        /// A hit at the piece's own height is the piece, or the floor it
        /// spawned in. The ground that can catch it is below that.
        /// </summary>
        public static bool GroundBelow(float pieceY, float hitY)
        {
            return hitY < pieceY - 0.35f;
        }

        /// <summary>
        /// A collapse leans over and stays there. An uncapped speed keeps
        /// spinning the shell after it should be lying down.
        /// </summary>
        public static float ToppleDegrees(float speed, float age)
        {
            if (age < 0f) age = 0f;
            float angle = speed * age;
            if (angle > 90f) return 90f;
            if (angle < -90f) return -90f;
            return angle;
        }
    }
}
