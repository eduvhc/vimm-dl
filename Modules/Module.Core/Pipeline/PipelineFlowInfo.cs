namespace Module.Core.Pipeline;

/// <summary>
/// A single step in a pipeline's flow, with its current status.
/// Each console pipeline defines its own step names and status mapping.
/// </summary>
public record PipelineFlowStep(string Name, string Status, string? Message, long? DurationMs = null);

/// <summary>
/// A pipeline's self-description of its flow for a given item.
/// Returned by IPipeline.BuildFlow — the host renders this generically.
/// </summary>
public record PipelineFlowInfo(string PipelineType, List<PipelineFlowStep> Steps, List<string> Actions);
