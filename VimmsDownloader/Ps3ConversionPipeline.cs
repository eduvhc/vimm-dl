using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using Ps3IsoTools;
using ZipExtractor;

class Ps3ConversionPipeline
{
    private readonly IHubContext<DownloadHub> _hub;
    private readonly ILogger<Ps3ConversionPipeline> _log;
    private readonly IConfiguration _config;
    private readonly ConcurrentDictionary<string, ConvertStatusUpdate> _statuses = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellations = new();
    private readonly HashSet<string> _convertedSet = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _convertedLock = new();
    private readonly Channel<ExtractJob> _extractQueue = Channel.CreateUnbounded<ExtractJob>();
    private readonly Channel<ConvertJob> _convertQueue = Channel.CreateUnbounded<ConvertJob>();
    private int _started;
    private string? _convertedFilePath;

    record ExtractJob(string ZipPath, string CompletedDir, string TempBaseDir);
    record ConvertJob(string JbFolder, string TempDir, string ZipName, string CompletedDir);

    public Ps3ConversionPipeline(IHubContext<DownloadHub> hub, ILogger<Ps3ConversionPipeline> log,
        IConfiguration config)
    {
        _hub = hub;
        _log = log;
        _config = config;
    }

    private int MaxParallelism => _config.GetValue("Ps3ConvertParallelism", 3);

    public bool IsConverted(string filename)
    {
        lock (_convertedLock)
            return _convertedSet.Contains(filename);
    }

    public void CleanupOrphans(string downloadBasePath)
    {
        var tempBaseDir = Path.Combine(downloadBasePath, "ps3_temp");
        var completedDir = Path.Combine(downloadBasePath, "completed");

        if (Directory.Exists(tempBaseDir))
        {
            foreach (var dir in Directory.GetDirectories(tempBaseDir))
            {
                var markerPath = Path.Combine(dir, ".extraction_complete");
                if (File.Exists(markerPath))
                {
                    // Extraction was completed before crash — resume from conversion
                    try
                    {
                        var lines = File.ReadAllLines(markerPath);
                        if (lines.Length >= 2)
                        {
                            var zipName = lines[0].Trim();
                            var jbFolder = lines[1].Trim();

                            if (Directory.Exists(jbFolder) && !IsConverted(zipName))
                            {
                                _log.LogInformation("Resuming conversion for previously extracted: {Zip}", zipName);
                                var queued = new ConvertStatusUpdate(zipName, "queued", "Resuming from extraction...");
                                _statuses[zipName] = queued;
                                var cts = new CancellationTokenSource();
                                _cancellations[zipName] = cts;
                                _convertQueue.Writer.TryWrite(new ConvertJob(jbFolder, dir, zipName, completedDir));
                                EnsureStarted();
                                continue;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "Failed to read extraction marker in {Dir}, cleaning up", dir);
                    }
                }

                // No valid marker or couldn't resume — orphaned, delete
                _log.LogInformation("Cleaning orphaned PS3 temp dir: {Path}", dir);
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        if (Directory.Exists(completedDir))
        {
            foreach (var f in Directory.GetFiles(completedDir, "temp_*.iso"))
            {
                _log.LogInformation("Cleaning orphaned temp ISO: {Path}", f);
                try { File.Delete(f); } catch { }
            }
        }

        _convertedFilePath = Path.Combine(completedDir ?? downloadBasePath, ".ps3converted");
        LoadConvertedList();
    }

    private void LoadConvertedList()
    {
        if (_convertedFilePath == null || !File.Exists(_convertedFilePath)) return;
        lock (_convertedLock)
        {
            foreach (var line in File.ReadAllLines(_convertedFilePath))
            {
                var name = line.Trim();
                if (name.Length == 0) continue;
                _convertedSet.Add(name);
                _statuses.TryAdd(name, new ConvertStatusUpdate(name, "done", "Previously converted"));
            }
        }
        _log.LogInformation("Loaded {Count} previously converted archives", _convertedSet.Count);
    }

    private void AddToConvertedList(string filename)
    {
        lock (_convertedLock)
        {
            if (!_convertedSet.Add(filename)) return;
            if (_convertedFilePath != null)
            {
                try
                {
                    var dir = Path.GetDirectoryName(_convertedFilePath);
                    if (dir != null) Directory.CreateDirectory(dir);
                    File.AppendAllText(_convertedFilePath, filename + "\n");
                }
                catch { }
            }
        }
    }

    public void MarkConverted(string filename)
    {
        _statuses[filename] = new ConvertStatusUpdate(filename, "done", "Marked as converted");
        AddToConvertedList(filename);
    }

    public bool Enqueue(string zipPath, string completedDir, string tempBaseDir, bool force = false)
    {
        var key = Path.GetFileName(zipPath);

        if (!force && IsConverted(key))
            return false;

        var queued = new ConvertStatusUpdate(key, "queued", "Waiting...");

        var result = _statuses.AddOrUpdate(key, queued, (_, existing) =>
            existing.Phase is "queued" or "extracting" or "extracted" or "converting"
                ? existing
                : queued);

        if (result != queued)
            return false;

        var cts = new CancellationTokenSource();
        _cancellations[key] = cts;
        _extractQueue.Writer.TryWrite(new ExtractJob(zipPath, completedDir, tempBaseDir));
        EnsureStarted();
        return true;
    }

    public List<ConvertStatusUpdate> GetStatuses() => _statuses.Values.ToList();

    public bool Abort(string filename)
    {
        if (_cancellations.TryRemove(filename, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            _statuses[filename] = new ConvertStatusUpdate(filename, "error", "Aborted by user");
            return true;
        }
        if (_statuses.TryGetValue(filename, out var s) && s.Phase == "queued")
        {
            _statuses[filename] = new ConvertStatusUpdate(filename, "error", "Aborted by user");
            return true;
        }
        return false;
    }

    private void EnsureStarted()
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) == 0)
        {
            var n = MaxParallelism;
            _log.LogInformation("Starting PS3 conversion pipeline with {N} workers per phase", n);
            for (var i = 0; i < n; i++)
            {
                _ = Task.Run(() => ExtractWorker());
                _ = Task.Run(() => ConvertWorker());
            }
        }
    }

    private Task EmitStatus(string zipName, string phase, string message)
        => EmitStatus(zipName, phase, message, null);

    private async Task EmitStatus(string zipName, string phase, string message, string? isoFilename)
    {
        var update = new ConvertStatusUpdate(zipName, phase, message, isoFilename);
        _statuses[zipName] = update;
        try
        {
            await _hub.Clients.All.SendAsync("ConvertStatus", update);
            await _hub.Clients.All.SendAsync("Status", $"[PS3] {zipName}: {message}");
        }
        catch { }
    }

    private async Task ExtractWorker()
    {
        await foreach (var job in _extractQueue.Reader.ReadAllAsync())
        {
            var zipName = Path.GetFileName(job.ZipPath);
            string? tempDir = null;

            _cancellations.TryGetValue(zipName, out var cts);
            var ct = cts?.Token ?? CancellationToken.None;

            if (ct.IsCancellationRequested)
            {
                _cancellations.TryRemove(zipName, out _);
                continue;
            }

            try
            {
                if (!File.Exists(job.ZipPath))
                {
                    await EmitStatus(zipName, "error", "Zip file no longer exists");
                    continue;
                }

                // Quick header check — reads only the archive index, not full content.
                // Catches truncated/corrupt archives (e.g. 50% downloaded) before extraction.
                await EmitStatus(zipName, "extracting", "Checking archive\u2026");
                var (headerOk, headerErr) = await ZipExtract.QuickCheckAsync(job.ZipPath, ct);
                if (!headerOk)
                {
                    var msg = ct.IsCancellationRequested ? "Aborted by user"
                        : $"Archive corrupted or incomplete: {headerErr}";
                    await EmitStatus(zipName, "error", msg);
                    if (!ct.IsCancellationRequested)
                        _log.LogError("Archive header check failed for {Zip}: {Error}", zipName, headerErr);
                    continue;
                }

                var zipSize = new FileInfo(job.ZipPath).Length;
                try
                {
                    var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(job.TempBaseDir))!);
                    if (drive.AvailableFreeSpace < zipSize * 3)
                    {
                        var freeMb = drive.AvailableFreeSpace / (1024.0 * 1024.0);
                        var needMb = zipSize * 3 / (1024.0 * 1024.0);
                        await EmitStatus(zipName, "error",
                            $"Not enough disk space ({freeMb:F0} MB free, ~{needMb:F0} MB needed)");
                        continue;
                    }
                }
                catch { }

                await EmitStatus(zipName, "extracting", "Extracting 0%");

                tempDir = Path.Combine(job.TempBaseDir, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(job.TempBaseDir);

                var (ok, error) = await ZipExtract.ExtractAsync(job.ZipPath, tempDir,
                    onProgress: pct => EmitStatus(zipName, "extracting", $"Extracting {pct}%").GetAwaiter().GetResult(),
                    ct);
                if (!ok)
                {
                    var msg = ct.IsCancellationRequested ? "Aborted by user" : $"Extraction failed: {error}";
                    await EmitStatus(zipName, "error", msg);
                    if (!ct.IsCancellationRequested)
                        _log.LogError("Extraction failed for {Zip}: {Error}", zipName, error);
                    try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
                    continue;
                }

                var jbFolder = Ps3IsoConverter.FindJbFolder(tempDir);
                if (jbFolder == null)
                {
                    await EmitStatus(zipName, "skipped", "No PS3 JB folder found in archive");
                    try { Directory.Delete(tempDir, true); } catch { }
                    continue;
                }

                // Write extraction marker so crash recovery can skip re-extraction
                try
                {
                    var markerPath = Path.Combine(tempDir, ".extraction_complete");
                    File.WriteAllText(markerPath, $"{zipName}\n{jbFolder}\n");
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to write extraction marker for {Zip}", zipName);
                }

                await EmitStatus(zipName, "extracted", "Queued for ISO conversion...");
                _convertQueue.Writer.TryWrite(new ConvertJob(jbFolder, tempDir, zipName, job.CompletedDir));
            }
            catch (OperationCanceledException)
            {
                await EmitStatus(zipName, "error", "Aborted by user");
                if (tempDir != null)
                    try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }
            catch (Exception ex)
            {
                await EmitStatus(zipName, "error", $"Extract error: {ex.Message}");
                _log.LogError(ex, "Extract failed for {Zip}", zipName);
                if (tempDir != null)
                    try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }
            finally
            {
                _cancellations.TryRemove(zipName, out _);
            }
        }
    }

    private async Task ConvertWorker()
    {
        await foreach (var job in _convertQueue.Reader.ReadAllAsync())
        {
            _cancellations.TryGetValue(job.ZipName, out var cts);
            var ct = cts?.Token ?? CancellationToken.None;

            if (ct.IsCancellationRequested)
            {
                _cancellations.TryRemove(job.ZipName, out _);
                try { if (Directory.Exists(job.TempDir)) Directory.Delete(job.TempDir, true); } catch { }
                continue;
            }

            try
            {
                await EmitStatus(job.ZipName, "converting", "Creating ISO...");

                var converter = new Ps3IsoConverter(new ConversionOptions());
                var result = await converter.ConvertFolderToIsoAsync(
                    job.JbFolder, job.CompletedDir,
                    onStatus: msg => EmitStatus(job.ZipName, "converting", msg).GetAwaiter().GetResult(),
                    ct);

                if (result.Success)
                {
                    var isoName = Path.GetFileName(result.IsoPath);
                    await EmitStatus(job.ZipName, "done", $"ISO ready: {isoName}", isoName);
                    AddToConvertedList(job.ZipName);
                    _log.LogInformation("PS3 ISO created: {IsoPath}", result.IsoPath);
                }
                else
                {
                    await EmitStatus(job.ZipName, "error", $"Conversion failed: {result.Error}");
                    _log.LogError("ISO conversion failed for {Zip}: {Error}", job.ZipName, result.Error);
                }
            }
            catch (OperationCanceledException)
            {
                await EmitStatus(job.ZipName, "error", "Aborted by user");
            }
            catch (Exception ex)
            {
                await EmitStatus(job.ZipName, "error", $"Convert error: {ex.Message}");
                _log.LogError(ex, "Convert failed for {Zip}", job.ZipName);
            }
            finally
            {
                _cancellations.TryRemove(job.ZipName, out _);
                // Async cleanup — don't block the worker while deleting GBs of temp files
                var tempToDelete = job.TempDir;
                _ = Task.Run(() =>
                {
                    try { if (Directory.Exists(tempToDelete)) Directory.Delete(tempToDelete, true); } catch { }
                });
            }
        }
    }
}
