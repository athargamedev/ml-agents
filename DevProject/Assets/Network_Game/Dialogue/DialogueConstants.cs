namespace Network_Game.Dialogue
{
    /// <summary>
    /// Centralized constants for Dialogue system magic numbers.
    /// Centralizing these makes tuning and reasoning about behavior easier.
    /// </summary>
    public static class DialogueConstants
    {
        // ── Request Queue ─────────────────────────────────────────────────────────
        public const int DefaultMaxHistoryMessages = 20;
        public const int DefaultMaxPendingRequests = 32;
        public const int DefaultMaxConcurrentRequests = 1;
        public const int DefaultMaxRequestsPerClient = 4;
        public const float DefaultMinSecondsBetweenRequests = 0.2f;
        public const float DefaultRequestTimeoutSeconds = 90f;

        // ── Retry ─────────────────────────────────────────────────────────────────
        public const int DefaultMaxRetries = 3;
        public const float DefaultRetryBackoffSeconds = 2f;
        public const float DefaultRetryJitterSeconds = 1f;

        // ── Warmup ────────────────────────────────────────────────────────────────
        public const float DefaultWarmupTimeoutSeconds = 60f;
        public const int DefaultDegradedWarmupFailureThreshold = 3;
        public const float DefaultWarmupRetryCooldownSeconds = 5f;

        // ── Broadcast ─────────────────────────────────────────────────────────────
        public const int DefaultBroadcastMaxCharacters = 180;

        // ── Statistics ───────────────────────────────────────────────────────────
        public const int DefaultLatencySampleWindow = 256;
        public const int DefaultRejectionReasonWindow = 256;
        public const float DefaultSummaryLogIntervalSeconds = 30f;

        // ── Player Customization ─────────────────────────────────────────────────
        public const int DefaultMaxPlayerCustomizationChars = 720;

        // ── Remote-specific ─────────────────────────────────────────────────────
        public const int DefaultRemoteMaxHistoryMessages = 6;
        public const int DefaultRemoteHistoryHardCapMessages = 8;
        public const int DefaultRemoteHistoryMessageCharBudget = 320;
        public const int DefaultRemoteUserPromptCharBudget = 520;
        public const int DefaultRemoteSystemPromptCharBudget = 8000;
        public const int DefaultRemoteSystemPromptHardCapChars = 3200;
        public const int DefaultRemoteMaxPlayerCustomizationChars = 220;
        public const float DefaultRemoteMinRequestTimeoutSeconds = 240f;

        // ── Stuck Conversation Recovery ────────────────────────────────────────────
        public const float StuckCheckInterval = 1f;
        public const float StuckConversationTimeout = 60f;

        // ── Gameplay Probe ───────────────────────────────────────────────────────
        public const int GameplayProbeClientRequestIdMin = 99400;
    }
}
