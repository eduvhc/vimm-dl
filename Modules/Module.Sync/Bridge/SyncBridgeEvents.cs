namespace Module.Sync.Bridge;

/// <summary>
/// All events emitted by the Sync module through its bridge.
/// The host decides how to handle each (SignalR, logging, etc).
/// </summary>
public abstract record SyncEvent;

public sealed record SyncProgressEvent(string Filename, double Percent, long Copied, long Total) : SyncEvent;
public sealed record SyncCompletedEvent(string Filename, bool Success, string? Error) : SyncEvent;
