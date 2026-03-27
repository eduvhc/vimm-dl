using Module.Core.Pipeline;

namespace Module.Ps3Pipeline;

/// <summary>
/// PS3-specific pipeline sub-phases. Extends PipelinePhase with
/// extraction and conversion phases unique to the PS3 workflow.
/// </summary>
public static class Ps3Phase
{
    // Universal (from PipelinePhase)
    public const string Queued = PipelinePhase.Queued;
    public const string Done = PipelinePhase.Done;
    public const string Error = PipelinePhase.Error;
    public const string Skipped = PipelinePhase.Skipped;

    // PS3-specific
    public const string Extracting = "extracting";
    public const string Extracted = "extracted";
    public const string Converting = "converting";
}
