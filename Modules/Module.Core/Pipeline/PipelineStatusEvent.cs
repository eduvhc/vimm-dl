namespace Module.Core.Pipeline;

/// <summary>
/// Generic status event for any pipeline item. Used by all console pipelines.
/// The Phase field carries both universal phases (queued/done/error) and
/// console-specific sub-phases (extracting/converting/etc).
/// </summary>
public record PipelineStatusEvent(
    string ItemName,
    string Phase,
    string Message,
    string? OutputFilename = null
);
