namespace Module.Core.Testing;

/// <summary>
/// Generic fake bridge that captures all events emitted by a module.
/// Use this in every module's test project — no need to write your own.
///
/// Thread-safe for async test scenarios.
///
/// Usage:
///   var bridge = new FakeBridge&lt;SyncEvent&gt;();
///   var service = new SyncService(bridge, logger);
///   // ... run service ...
///   Assert.AreEqual(1, bridge.Of&lt;SyncCompletedEvent&gt;().Count);
/// </summary>
public class FakeBridge<TEvent> : IModuleBridge<TEvent>
{
    private readonly List<TEvent> _events = [];
    private readonly object _lock = new();

    /// <summary>All captured events in order.</summary>
    public IReadOnlyList<TEvent> AllEvents
    {
        get { lock (_lock) return _events.ToList(); }
    }

    /// <summary>Filter events by concrete type.</summary>
    public IReadOnlyList<T> Of<T>() where T : TEvent
    {
        lock (_lock) return _events.OfType<T>().ToList();
    }

    /// <summary>Last event of a specific type, or null.</summary>
    public T? Last<T>() where T : class, TEvent
    {
        lock (_lock) return _events.OfType<T>().LastOrDefault();
    }

    /// <summary>Total count of all captured events.</summary>
    public int Count
    {
        get { lock (_lock) return _events.Count; }
    }

    public Task SendAsync(TEvent evt)
    {
        lock (_lock) _events.Add(evt);
        return Task.CompletedTask;
    }

    /// <summary>Clear all captured events.</summary>
    public void Clear()
    {
        lock (_lock) _events.Clear();
    }
}
