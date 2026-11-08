using System;

namespace NOReplay
{
    /// <summary>
    /// The recorder writes tacview id = persistent id + 1. The game treats
    /// persistent id 0 as invalid, so file id "1" is that early registration.
    /// It is a duplicate only when another aircraft already has the same name.
    /// </summary>
    public static class GhostTrack
    {
        public static bool SkipInvalidDuplicate(string? id, bool sameNameAircraft)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (!string.Equals(id, "1", StringComparison.OrdinalIgnoreCase)) return false;
            return sameNameAircraft;
        }
    }
}
