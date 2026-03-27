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
}
