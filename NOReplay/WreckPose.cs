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
    }
}
