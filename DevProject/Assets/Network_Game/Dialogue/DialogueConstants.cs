namespace Network_Game.Dialogue
{
    /// <summary>
    /// Centralized constants for Dialogue system magic numbers.
    /// Centralizing these makes tuning and reasoning about behavior easier.
    /// </summary>
    public static class DialogueConstants
    {
        /// <summary>
        /// Configuration constants related to request queuing and history management.
        /// </summary>
        public static class RequestQueue
        {
            /// <summary>
            /// Default maximum number of history messages to retain per conversation. Default is 20.
            /// </summary>
            public const int DefaultMaxHistoryMessages = 20;
            
            /// <summary>
            /// Default maximum number of pending requests in the queue. Default is 32.
            /// </summary>
            public const int DefaultMaxPendingRequests = 32;
            
            /// <summary>
            /// Default maximum number of concurrent requests allowed. Default is 1.
            /// </summary>
            public const int DefaultMaxConcurrentRequests = 1;
            
            /// <summary>
            /// Default maximum number of requests per client. Default is 4.
            /// </summary>
            public const int DefaultMaxRequestsPerClient = 4;
            
            /// <summary>
            /// Default minimum seconds to wait between requests. Default is 0.2f.
            /// </summary>
            public const float DefaultMinSecondsBetweenRequests = 0.2f;
            
            /// <summary>
            /// Default timeout for requests in seconds. Default is 90f.
            /// </summary>
            public const float DefaultRequestTimeoutSeconds = 90f;
        }

        /// <summary>
        /// Configuration constants related to retry mechanisms.
        /// </summary>
        public static class Retry
        {
            /// <summary>
            /// Default maximum number of retries for failed requests. Default is 3.
            /// </summary>
            public const int DefaultMaxRetries = 3;
            
            /// <summary>
            /// Default backoff time in seconds between retries. Default is 2f.
            /// </summary>
            public const float DefaultRetryBackoffSeconds = 2f;
            
            /// <summary>
            /// Default jitter time in seconds to add randomness to retry timing. Default is 1f.
            /// </summary>
            public const float DefaultRetryJitterSeconds = 1f;
        }

        /// <summary>
        /// Configuration constants related to system warmup procedures.
        /// </summary>
        public static class Warmup
        {
            /// <summary>
            /// Default timeout for warmup procedures in seconds. Default is 60f.
            /// </summary>
            public const float DefaultWarmupTimeoutSeconds = 60f;
            
            /// <summary>
            /// Default threshold for degraded warmup failures. Default is 3.
            /// </summary>
            public const int DefaultDegradedWarmupFailureThreshold = 3;
            
            /// <summary>
            /// Default cooldown time in seconds between warmup retries. Default is 5f.
            /// </summary>
            public const float DefaultWarmupRetryCooldownSeconds = 5f;
        }

        /// <summary>
        /// Configuration constants related to broadcast messaging.
        /// </summary>
        public static class Broadcast
        {
            /// <summary>
            /// Default maximum number of characters allowed in a broadcast message. Default is 180.
            /// </summary>
            public const int DefaultBroadcastMaxCharacters = 180;
        }

        /// <summary>
        /// Configuration constants related to statistics collection and reporting.
        /// </summary>
        public static class Statistics
        {
            /// <summary>
            /// Default size of the latency sample window. Default is 256.
            /// </summary>
            public const int DefaultLatencySampleWindow = 256;
            
            /// <summary>
            /// Default size of the rejection reason tracking window. Default is 256.
            /// </summary>
            public const int DefaultRejectionReasonWindow = 256;
            
            /// <summary>
            /// Default interval in seconds for summary logs. Default is 30f.
            /// </summary>
            public const float DefaultSummaryLogIntervalSeconds = 30f;
        }

        /// <summary>
        /// Configuration constants related to player customization features.
        /// </summary>
        public static class PlayerCustomization
        {
            /// <summary>
            /// Default maximum number of characters allowed for player customization. Default is 720.
            /// </summary>
            public const int DefaultMaxPlayerCustomizationChars = 720;
        }

        /// <summary>
        /// Configuration constants specific to remote operations.
        /// </summary>
        public static class Remote
        {
            /// <summary>
            /// Default maximum number of history messages for remote operations. Default is 6.
            /// </summary>
            public const int DefaultRemoteMaxHistoryMessages = 6;
            
            /// <summary>
            /// Default hard cap on history messages for remote operations. Default is 8.
            /// </summary>
            public const int DefaultRemoteHistoryHardCapMessages = 8;
            
            /// <summary>
            /// Default character budget per history message for remote operations. Default is 320.
            /// </summary>
            public const int DefaultRemoteHistoryMessageCharBudget = 320;
            
            /// <summary>
            /// Default character budget for user prompts in remote operations. Default is 520.
            /// </summary>
            public const int DefaultRemoteUserPromptCharBudget = 520;
            
            /// <summary>
            /// Default character budget for system prompts in remote operations. Default is 8000.
            /// </summary>
            public const int DefaultRemoteSystemPromptCharBudget = 8000;
            
            /// <summary>
            /// Default hard cap on system prompt characters for remote operations. Default is 3200.
            /// </summary>
            public const int DefaultRemoteSystemPromptHardCapChars = 3200;
            
            /// <summary>
            /// Default maximum number of characters for player customization in remote operations. Default is 220.
            /// </summary>
            public const int DefaultRemoteMaxPlayerCustomizationChars = 220;
            
            /// <summary>
            /// Default minimum request timeout in seconds for remote operations. Default is 240f.
            /// </summary>
            public const float DefaultRemoteMinRequestTimeoutSeconds = 240f;
        }

        /// <summary>
        /// Configuration constants related to stuck conversation recovery.
        /// </summary>
        public static class StuckConversationRecovery
        {
            /// <summary>
            /// Interval in seconds to check for stuck conversations. Default is 1f.
            /// </summary>
            public const float StuckCheckInterval = 1f;
            
            /// <summary>
            /// Timeout in seconds before considering a conversation stuck. Default is 60f.
            /// </summary>
            public const float StuckConversationTimeout = 60f;
        }

        /// <summary>
        /// Configuration constants related to gameplay probe functionality.
        /// </summary>
        public static class GameplayProbe
        {
            /// <summary>
            /// Minimum client request ID for gameplay probe operations. Default is 99400.
            /// </summary>
            public const int GameplayProbeClientRequestIdMin = 99400;
        }
    }
}
