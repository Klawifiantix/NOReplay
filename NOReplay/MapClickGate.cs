namespace NOReplay
{
    /// <summary>
    /// Shift-click during a replay must not run the game's map click.
    /// That click teleports the camera and calls SetFollowingUnit, which
    /// leaves free flight, before any postfix can see the free-flight state.
    /// </summary>
    public static class MapClickGate
    {
        public readonly struct Choice
        {
            public readonly bool RunGameClick;
            public readonly bool SelectInPlace;

            public Choice(bool runGameClick, bool selectInPlace)
            {
                RunGameClick = runGameClick;
                SelectInPlace = selectInPlace;
            }
        }

        public static Choice Decide(bool playingReplay, bool shiftHeld, bool iconSuppressed)
        {
            if (iconSuppressed) return new Choice(false, false);
            if (playingReplay && shiftHeld) return new Choice(false, true);
            return new Choice(true, false);
        }
    }
}
