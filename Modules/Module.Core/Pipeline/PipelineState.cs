using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Module.Core.Pipeline;

/// <summary>
/// Shared pipeline state: status tracking, converted list, abort support.
/// Concrete class — console pipelines hold an instance, not inherit from it.
/// </summary>
public class PipelineState
{
    private readonly IModuleBridge<PipelineStatusEvent> _bridge;
    private readonly ILogger _log;
    public readonly ConcurrentDictionary<string, PipelineStatusEvent> Statuses = new();
    public readonly ConcurrentDictionary<string, CancellationTokenSource> Cancellations = new();
    private readonly ConcurrentDictionary<string, string> _correlationIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<(string Phase, long TimestampMs)>> _phaseTimings = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _convertedSet = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _convertedLock = new();

    public PipelineState(IModuleBridge<PipelineStatusEvent> bridge, ILogger log)
    {
        _bridge = bridge;
        _log = log;
    }

    public ILogger Log => _log;

    public async Task EmitStatus(string name, string phase, string message, string? outputFilename = null)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Generate new correlation ID and reset timings on pipeline run start
        if (phase == PipelinePhase.Queued)
        {
            _correlationIds[name] = Guid.NewGuid().ToString("N")[..12];
            _phaseTimings[name] = [];
        }

        _correlationIds.TryGetValue(name, out var correlationId);

        // Track phase transition timing
        if (_phaseTimings.TryGetValue(name, out var timings))
            timings.Add((phase, now));

        var evt = new PipelineStatusEvent(name, phase, message, outputFilename, correlationId);
        Statuses[name] = evt;
        try { await _bridge.SendAsync(evt); } catch { }
    }

    /// <summary>
    /// Get step durations for an item's current pipeline run.
    /// Returns a dictionary of phase → duration in milliseconds.
    /// For active phases, duration is elapsed time since phase started.
    /// </summary>
    public Dictionary<string, long> GetStepDurations(string name)
    {
        if (!_phaseTimings.TryGetValue(name, out var timings) || timings.Count == 0)
            return new();

        var durations = new Dictionary<string, long>();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        for (int i = 0; i < timings.Count; i++)
        {
            var (phase, startMs) = timings[i];
            var endMs = i + 1 < timings.Count ? timings[i + 1].TimestampMs : now;
            durations[phase] = endMs - startMs;
        }

        return durations;
    }

    public bool IsConverted(string name)
    {
        lock (_convertedLock)
            return _convertedSet.Contains(name);
    }

    public void AddToConvertedList(string name)
    {
        lock (_convertedLock)
            _convertedSet.Add(name);
    }

    public void MarkConverted(string name)
    {
        Statuses[name] = new PipelineStatusEvent(name, PipelinePhase.Done, "Marked as converted");
        AddToConvertedList(name);
    }

    public void SeedConverted(IEnumerable<string> names)
    {
        lock (_convertedLock)
        {
            foreach (var name in names)
            {
                _convertedSet.Add(name);
                Statuses.TryAdd(name, new PipelineStatusEvent(name, PipelinePhase.Done, "Previously converted"));
            }
        }
        _log.LogInformation("Loaded {Count} previously converted items", _convertedSet.Count);
    }

    public List<PipelineStatusEvent> GetStatuses() => Statuses.Values.ToList();

    public bool Abort(string name)
    {
        if (Cancellations.TryRemove(name, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            Statuses[name] = new PipelineStatusEvent(name, PipelinePhase.Error, "Aborted by user");
            return true;
        }
        if (Statuses.TryGetValue(name, out var s) && s.Phase == PipelinePhase.Queued)
        {
            Statuses[name] = new PipelineStatusEvent(name, PipelinePhase.Error, "Aborted by user");
            return true;
        }
        return false;
    }
}
