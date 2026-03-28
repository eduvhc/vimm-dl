using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Module.Core;
using Module.Core.Pipeline;
using Module.Extractor;
using Module.Ps3IsoTools;

namespace Module.Ps3Pipeline;

/// <summary>
/// PS3 JB Folder pipeline: extract archive → find PS3_GAME → makeps3iso → patchps3iso → .iso
/// </summary>
public class Ps3JbFolderPipeline
{
    private readonly PipelineState _state;
    private readonly Ps3DecIsoPipeline _decIso;
    private readonly Channel<ExtractJob> _extractQueue = Channel.CreateUnbounded<ExtractJob>();
    private readonly Channel<ConvertJob> _convertQueue = Channel.CreateUnbounded<ConvertJob>();
    private int _started;
    private int _maxParallelism = 3;

    record ExtractJob(string ZipPath, string CompletedDir, string TempBaseDir);
    record ConvertJob(string JbFolder, string TempDir, string ZipName, string CompletedDir);

    public Ps3JbFolderPipeline(PipelineState state, Ps3DecIsoPipeline decIso)
    {
        _state = state;
        _decIso = decIso;
    }

    public void Configure(int maxParallelism) => _maxParallelism = maxParallelism;

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
                    try
                    {
                        var lines = File.ReadAllLines(markerPath);
                        if (lines.Length >= 2)
                        {
                            var zipName = lines[0].Trim();
                            var jbFolder = lines[1].Trim();
                            if (Directory.Exists(jbFolder) && !_state.IsConverted(zipName))
                            {
                                _state.Log.LogInformation("Resuming conversion for previously extracted: {Zip}", zipName);
                                _state.Statuses[zipName] = new PipelineStatusEvent(zipName, Ps3Phase.Queued, "Resuming from extraction...");
                                var cts = new CancellationTokenSource();
                                _state.Cancellations[zipName] = cts;
                                _convertQueue.Writer.TryWrite(new ConvertJob(jbFolder, dir, zipName, completedDir));
                                EnsureStarted();
                                continue;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _state.Log.LogWarning(ex, "Failed to read extraction marker in {Dir}, cleaning up", dir);
                    }
                }
                _state.Log.LogInformation("Cleaning orphaned PS3 temp dir: {Path}", dir);
                FileOps.TryDeleteDirectory(dir);
            }
        }

        if (Directory.Exists(completedDir))
        {
            foreach (var f in Directory.GetFiles(completedDir, "temp_*.iso"))
            {
                _state.Log.LogInformation("Cleaning orphaned temp ISO: {Path}", f);
                FileOps.TryDelete(f);
            }
        }

    }

    public bool Enqueue(string zipPath, string completedDir, string tempBaseDir, bool force = false)
    {
        var key = Path.GetFileName(zipPath);
        if (!force && _state.IsConverted(key)) return false;

        var queued = new PipelineStatusEvent(key, Ps3Phase.Queued, "Waiting...");
        var result = _state.Statuses.AddOrUpdate(key, queued, (_, existing) =>
            PipelinePhase.IsActive(existing.Phase) ? existing : queued);
        if (result != queued) return false;

        var cts = new CancellationTokenSource();
        _state.Cancellations[key] = cts;
        _extractQueue.Writer.TryWrite(new ExtractJob(zipPath, completedDir, tempBaseDir));
        EnsureStarted();
        return true;
    }

    private void EnsureStarted()
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) == 0)
        {
            _state.Log.LogInformation("Starting PS3 JB folder pipeline with {N} workers per phase", _maxParallelism);
            for (var i = 0; i < _maxParallelism; i++)
            {
                _ = Task.Run(ExtractWorker);
                _ = Task.Run(ConvertWorker);
            }
        }
    }

    private async Task ExtractWorker()
    {
        await foreach (var job in _extractQueue.Reader.ReadAllAsync())
        {
            var zipName = Path.GetFileName(job.ZipPath);
            string? tempDir = null;
            _state.Cancellations.TryGetValue(zipName, out var cts);
            var ct = cts?.Token ?? CancellationToken.None;
            if (ct.IsCancellationRequested) { _state.Cancellations.TryRemove(zipName, out _); continue; }

            try
            {
                if (!File.Exists(job.ZipPath))
                {
                    await _state.EmitStatus(zipName, Ps3Phase.Error, "Zip file no longer exists");
                    continue;
                }

                await _state.EmitStatus(zipName, Ps3Phase.Extracting, "Checking archive\u2026");
                var headerCheck = await ZipExtract.QuickCheckAsync(job.ZipPath, ct);
                if (!headerCheck.IsOk)
                {
                    await _state.EmitStatus(zipName, Ps3Phase.Error,
                        ct.IsCancellationRequested ? "Aborted by user" : $"Archive corrupted: {headerCheck.Error}");
                    continue;
                }

                var zipSize = new FileInfo(job.ZipPath).Length;
                try
                {
                    var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(job.TempBaseDir))!);
                    if (drive.AvailableFreeSpace < zipSize * 3)
                    {
                        await _state.EmitStatus(zipName, Ps3Phase.Error,
                            $"Not enough disk space ({drive.AvailableFreeSpace / 1048576.0:F0} MB free, ~{zipSize * 3 / 1048576.0:F0} MB needed)");
                        continue;
                    }
                }
                catch { }

                await _state.EmitStatus(zipName, Ps3Phase.Extracting, "Extracting 0%");
                tempDir = Path.Combine(job.TempBaseDir, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(job.TempBaseDir);

                var extractResult = await ZipExtract.ExtractAsync(job.ZipPath, tempDir,
                    onProgress: pct => _state.EmitStatus(zipName, Ps3Phase.Extracting, $"Extracting {pct}%").GetAwaiter().GetResult(), ct);
                if (!extractResult.IsOk)
                {
                    await _state.EmitStatus(zipName, Ps3Phase.Error,
                        ct.IsCancellationRequested ? "Aborted by user" : $"Extraction failed: {extractResult.Error}");
                    FileOps.TryDeleteDirectory(tempDir);
                    continue;
                }

                var jbFolder = Ps3IsoConverter.FindJbFolder(tempDir);
                if (jbFolder == null)
                {
                    var handled = await _decIso.HandleExtractedArchive(zipName, tempDir, job.CompletedDir);
                    if (!handled.IsOk)
                        await _state.EmitStatus(zipName, Ps3Phase.Skipped, "No PS3 JB folder or ISO found in archive");
                    FileOps.TryDeleteDirectory(tempDir);
                    continue;
                }

                var markerResult = FileOps.TryWriteAllText(
                    Path.Combine(tempDir, ".extraction_complete"), $"{zipName}\n{jbFolder}\n");
                if (!markerResult.IsOk)
                    _state.Log.LogWarning("Failed to write extraction marker for {Zip}: {Error}", zipName, markerResult.Error);

                await _state.EmitStatus(zipName, Ps3Phase.Extracted, "Queued for ISO conversion...");
                _convertQueue.Writer.TryWrite(new ConvertJob(jbFolder, tempDir, zipName, job.CompletedDir));
            }
            catch (OperationCanceledException)
            {
                await _state.EmitStatus(zipName, Ps3Phase.Error, "Aborted by user");
                if (tempDir != null) FileOps.TryDeleteDirectory(tempDir);
            }
            finally { _state.Cancellations.TryRemove(zipName, out _); }
        }
    }

    private async Task ConvertWorker()
    {
        await foreach (var job in _convertQueue.Reader.ReadAllAsync())
        {
            _state.Cancellations.TryGetValue(job.ZipName, out var cts);
            var ct = cts?.Token ?? CancellationToken.None;
            if (ct.IsCancellationRequested)
            {
                _state.Cancellations.TryRemove(job.ZipName, out _);
                FileOps.TryDeleteDirectory(job.TempDir);
                continue;
            }

            try
            {
                await _state.EmitStatus(job.ZipName, Ps3Phase.Converting, "Creating ISO...");
                var converter = new Ps3IsoConverter(new ConversionOptions());
                var result = await converter.ConvertFolderToIsoAsync(job.JbFolder, job.CompletedDir,
                    onStatus: msg => _state.EmitStatus(job.ZipName, Ps3Phase.Converting, msg).GetAwaiter().GetResult(), ct);

                if (result.IsOk)
                {
                    var isoName = Path.GetFileName(result.Value);
                    await _state.EmitStatus(job.ZipName, Ps3Phase.Done, $"ISO ready: {isoName}", isoName);
                    _state.AddToConvertedList(job.ZipName);
                    _state.Log.LogInformation("PS3 ISO created: {IsoPath}", result.Value);
                }
                else
                {
                    await _state.EmitStatus(job.ZipName, Ps3Phase.Error, $"Conversion failed: {result.Error}");
                    _state.Log.LogError("ISO conversion failed for {Zip}: {Error}", job.ZipName, result.Error);
                }
            }
            catch (OperationCanceledException)
            {
                await _state.EmitStatus(job.ZipName, Ps3Phase.Error, "Aborted by user");
            }
            finally
            {
                _state.Cancellations.TryRemove(job.ZipName, out _);
                var tempToDelete = job.TempDir;
                _ = Task.Run(() => FileOps.TryDeleteDirectory(tempToDelete));
            }
        }
    }
}
