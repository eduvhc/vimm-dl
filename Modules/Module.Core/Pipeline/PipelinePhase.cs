namespace Module.Core.Pipeline;

/// <summary>
/// Universal pipeline lifecycle phases. Console-specific sub-phases
/// (like "extracting", "converting") are valid phase strings too —
/// these are the lifecycle boundaries that matter for the IPipeline contract.
/// </summary>
public static class PipelinePhase
{
    public const string Queued = "queued";
    public const string Done = "done";
    public const string Error = "error";
    public const string Skipped = "skipped";

    public static bool IsTerminal(string? phase) =>
        phase is Done or Error or Skipped;

    public static bool IsActive(string? phase) =>
        phase != null && !IsTerminal(phase);
}
