namespace NOReplay
{
    /// <summary>
    /// A replay lerps from one stored pose to the next. A unit that sits and
    /// then moves has to repeat the sitting pose at the moment motion starts,
    /// or the whole pause becomes a glide. Rest is the same idea in reverse:
    /// one slow sample is the top of a throw, not the ground.
    /// </summary>
    public static class PoseHold
    {
        public const float PauseSeconds = 2f;
        public const float RestSeconds = 1f;
        public const float RestSpeedSqr = 0.25f;

        public static bool Pause(float sinceWrite)
        {
            return sinceWrite >= 0f && sinceWrite > PauseSeconds;
        }

        public static bool Rested(float stillFor, float speedSqr)
        {
            return stillFor >= RestSeconds && speedSqr <= RestSpeedSqr;
        }
    }
}
