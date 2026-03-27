using Module.Core.Testing;
using Module.Sync.Bridge;

namespace Module.Sync.Tests.Helpers;

/// <summary>
/// Typed alias over FakeBridge for Sync module tests.
/// Adds convenience accessors for common Sync event types.
/// </summary>
public class FakeSyncBridge : FakeBridge<SyncEvent>, ISyncBridge
{
    public IReadOnlyList<SyncProgressEvent> ProgressEvents => Of<SyncProgressEvent>();
    public IReadOnlyList<SyncCompletedEvent> CompletedEvents => Of<SyncCompletedEvent>();
    public SyncCompletedEvent? LastCompleted => Last<SyncCompletedEvent>();
}
