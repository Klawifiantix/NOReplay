namespace NOReplay
{
    /// <summary>
    /// A thrown part is removed from the replay clock, including when the
    /// shooter ghost has already been switched off.
    /// </summary>
    public static class DebrisLife
    {
        public static bool Expired(bool playing, float now, float born, float life)
        {
            if (!playing) return true;
            if (now + 0.02f < born) return true;
            return life > 0f && now > born + life;
        }
    }
}
