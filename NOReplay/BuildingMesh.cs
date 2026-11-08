namespace NOReplay
{
    /// <summary>
    /// The intact building mesh is hidden at removal. Fire, smoke, and trails
    /// are renderers too, and those stay. A mesh under a particle script is
    /// still the tank, so the parent script does not keep it.
    /// </summary>
    public static class BuildingMesh
    {
        public static bool IsIntactMesh(string? typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return false;
            if (typeName == "ParticleSystemRenderer") return false;
            if (typeName == "TrailRenderer") return false;
            if (typeName == "LineRenderer") return false;
            return true;
        }
    }
}
