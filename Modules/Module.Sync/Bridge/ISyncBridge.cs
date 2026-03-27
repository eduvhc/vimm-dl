using Module.Core;

namespace Module.Sync.Bridge;

/// <summary>
/// Bridge contract for the Sync module.
/// The host implements this to receive sync events.
/// </summary>
public interface ISyncBridge : IModuleBridge<SyncEvent>;
