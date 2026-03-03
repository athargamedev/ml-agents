namespace Network_Game.Dialogue
{
    /// <summary>
    /// Centralized constants for DialogueEffectSpatialResolver.
    /// </summary>
    public static class SpatialResolverConstants
    {
        public const int MaxOverlapBuffer = 64;
        public const int MaxLoSBuffer = 16;
        public const int SearchRings = 4;
        public const int SamplesPerRing = 12;

        // Minimum thresholds to avoid division by zero or useless operations
        public const float MinClearanceRadius = 0.01f;
        public const float MinProbeDistance = 0.05f;
        public const float MinForwardSqrMagnitude = 0.0001f;
        public const float MinLoSDistanceSqr = 0.01f;

        // Default step multiplier for nearby position search
        public const float NearbySearchStepMultiplier = 0.75f;
        public const float NearbySearchMinStep = 0.35f;
    }
}
