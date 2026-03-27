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
    private readonly HashSet<string> _convertedSet = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _convertedLock = new();
    private string? _convertedFilePath;

    public PipelineState(IModuleBridge<PipelineStatusEvent> bridge, ILogger log)
    {
        _bridge = bridge;
        _log = log;
    }

    public ILogger Log => _log;

    public async Task EmitStatus(string name, string phase, string message, string? outputFilename = null)
    {
        var evt = new PipelineStatusEvent(name, phase, message, outputFilename);
        Statuses[name] = evt;
        try { await _bridge.SendAsync(evt); } catch { }
    }

    public bool IsConverted(string name)
    {
        lock (_convertedLock)
            return _convertedSet.Contains(name);
    }

    public void AddToConvertedList(string name)
    {
        lock (_convertedLock)
        {
            if (!_convertedSet.Add(name)) return;
            if (_convertedFilePath != null)
            {
                try
                {
                    var dir = Path.GetDirectoryName(_convertedFilePath);
                    if (dir != null) Directory.CreateDirectory(dir);
                    File.AppendAllText(_convertedFilePath, name + "\n");
                }
                catch { }
            }
        }
    }

    public void MarkConverted(string name)
    {
        Statuses[name] = new PipelineStatusEvent(name, PipelinePhase.Done, "Marked as converted");
        AddToConvertedList(name);
    }

    public void SetConvertedFilePath(string path)
    {
        _convertedFilePath = path;
    }

    public void LoadConvertedList()
    {
        if (_convertedFilePath == null || !File.Exists(_convertedFilePath)) return;
        lock (_convertedLock)
        {
            foreach (var line in File.ReadAllLines(_convertedFilePath))
            {
                var name = line.Trim();
                if (name.Length == 0) continue;
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
