namespace Network_Game.Diagnostics
{
    /// <summary>
    /// Zero-overhead bridge from the inference pipeline to DebugWatchdog.
    /// Calls to ReportInference are stripped in non-UNITY_EDITOR builds by [Conditional].
    /// </summary>
    internal static class InferenceWatchReporter
    {
        internal static DebugWatchdog ActiveWatchdog { get; set; }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        internal static void ReportInference(string prompt, string response, float elapsedMs)
        {
            ActiveWatchdog?.RecordInference(prompt, response, elapsedMs);
        }
    }
}
