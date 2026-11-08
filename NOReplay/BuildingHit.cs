namespace NOReplay
{
    /// <summary>
    /// A building hit uses the gun burst that is still firing when the building
    /// comes down. The extra fraction covers a burst that ends as the last
    /// round arrives. It does not attach an unrelated later burst.
    /// </summary>
    public static class BuildingHit
    {
        public const float Tail = 0.4f;

        public static bool Covers(float start, float end, float removed)
        {
            if (removed < 0f || start < 0f) return false;
            if (end < start) end = start;
            return removed >= start && removed <= end + Tail;
        }
    }
}
