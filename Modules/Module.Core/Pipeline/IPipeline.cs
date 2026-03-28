namespace Module.Core.Pipeline;

/// <summary>
/// Generic pipeline contract. The host depends on this for status queries,
/// abort, and converted tracking. Enqueue is NOT here — each console pipeline
/// has its own enqueue signature with different parameters.
/// </summary>
public interface IPipeline
{
    List<PipelineStatusEvent> GetStatuses();
    bool Abort(string itemName);
    bool IsConverted(string itemName);
    void MarkConverted(string itemName);

    /// <summary>
    /// Build the flow/trace for a specific item given its current state.
    /// Each console pipeline defines its own step order and status mapping.
    /// Returns null if no trace applies (e.g., wrong platform or no activity).
    /// </summary>
    PipelineFlowInfo? BuildFlow(string? phase, string? message, bool fileExists);

    /// <summary>
    /// Check whether a completed download is a duplicate using console-specific rules.
    /// Each pipeline knows its own file patterns, active phases, and filesystem layout.
    /// Returns null if not a duplicate (files missing, safe to re-download).
    /// </summary>
    DuplicateCheckResult? CheckDuplicate(string completedDir, string? filename, string? isoFilename, string? convPhase);

    /// <summary>
    /// Get step durations for an item's current pipeline run.
    /// Returns phase → duration in milliseconds. Active phases show elapsed time.
    /// </summary>
    Dictionary<string, long> GetStepDurations(string itemName);
}
