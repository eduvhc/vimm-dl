using Microsoft.Extensions.Logging;
using Module.Sync.Bridge;

namespace Module.Sync;

public class SyncService
{
    private readonly ISyncBridge _bridge;
    private readonly ILogger<SyncService> _log;
    private CancellationTokenSource? _cts;

    private string _downloadPath = "";
    private string _syncPath = "";

    public bool IsCopying { get; private set; }
    public string? CurrentFile { get; private set; }
    public double CurrentProgress { get; private set; }

    public SyncService(ISyncBridge bridge, ILogger<SyncService> log)
    {
        _bridge = bridge;
        _log = log;
    }

    public void Configure(string downloadPath, string syncPath)
    {
        _downloadPath = downloadPath;
        _syncPath = syncPath;
    }

    public string GetSyncPath() => _syncPath;
    public void SetSyncPath(string path) => _syncPath = path;

    internal string GetCompletedDir()
    {
        var dlPath = _downloadPath;
        if (string.IsNullOrWhiteSpace(dlPath))
            dlPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        return Path.Combine(dlPath, "completed");
    }

    public SyncCompareResponse Compare()
    {
        var syncPath = _syncPath;

        if (string.IsNullOrWhiteSpace(syncPath))
            return new SyncCompareResponse(syncPath ?? "", false, [], [], [], null, null, null);

        if (!IsPathAccessible(syncPath))
            return new SyncCompareResponse(syncPath, false, [], [], [], null, null,
                "Target path is not accessible \u2014 drive may be disconnected");

        var completedDir = GetCompletedDir();

        if (!IsPathAccessible(completedDir))
            return new SyncCompareResponse(syncPath, true, [], [], [],
                null, GetDiskInfo(syncPath), null);

        List<FileInfo> sourceList, targetList;
        try { sourceList = SafeGetIsos(completedDir); }
        catch (IOException ex)
        {
            _log.LogWarning(ex, "Source path became inaccessible during sync compare");
            return new SyncCompareResponse(syncPath, true, [], [], [],
                null, GetDiskInfo(syncPath),
                "Source path became inaccessible \u2014 drive may have been disconnected");
        }

        try { targetList = SafeGetIsos(syncPath); }
        catch (IOException ex)
        {
            _log.LogWarning(ex, "Target path became inaccessible during sync compare");
            return new SyncCompareResponse(syncPath, false, [], [], [], null, null,
                "Target path became inaccessible \u2014 drive may have been disconnected");
        }

        var sourceIsos = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var fi in sourceList) sourceIsos.TryAdd(fi.Name, fi);

        var targetIsos = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var fi in targetList) targetIsos.TryAdd(fi.Name, fi);

        var newFiles = new List<SyncFileInfo>();
        var syncedFiles = new List<SyncFileInfo>();
        var targetOnly = new List<SyncFileInfo>();

        foreach (var (name, fi) in sourceIsos)
        {
            if (targetIsos.ContainsKey(name))
                syncedFiles.Add(new SyncFileInfo(name, SafeLength(fi)));
            else
                newFiles.Add(new SyncFileInfo(name, SafeLength(fi)));
        }

        foreach (var (name, fi) in targetIsos)
        {
            if (!sourceIsos.ContainsKey(name))
                targetOnly.Add(new SyncFileInfo(name, SafeLength(fi)));
        }

        return new SyncCompareResponse(syncPath, true, newFiles, syncedFiles, targetOnly,
            GetDiskInfo(completedDir), GetDiskInfo(syncPath), null);
    }

    public async Task CopyFileAsync(string filename, CancellationToken ct = default)
    {
        var syncPath = _syncPath;
        if (string.IsNullOrWhiteSpace(syncPath))
        {
            await Emit(new SyncCompletedEvent(filename, false, "Sync path is not configured"));
            return;
        }

        if (!IsPathAccessible(syncPath))
        {
            await Emit(new SyncCompletedEvent(filename, false, "Target drive is not accessible \u2014 it may be disconnected"));
            return;
        }

        var source = Path.Combine(GetCompletedDir(), filename);
        if (!File.Exists(source))
        {
            await Emit(new SyncCompletedEvent(filename, false, $"Source file not found: {filename}"));
            return;
        }

        long sourceSize;
        try { sourceSize = new FileInfo(source).Length; }
        catch (IOException)
        {
            await Emit(new SyncCompletedEvent(filename, false, "Cannot read source file \u2014 drive may be disconnected"));
            return;
        }

        try
        {
            var targetRoot = Path.GetPathRoot(syncPath);
            if (!string.IsNullOrEmpty(targetRoot))
            {
                var drive = new DriveInfo(targetRoot);
                if (drive.IsReady && drive.AvailableFreeSpace < sourceSize)
                {
                    await Emit(new SyncCompletedEvent(filename, false,
                        $"Not enough space on target drive ({FormatBytes(drive.AvailableFreeSpace)} free, need {FormatBytes(sourceSize)})"));
                    return;
                }
            }
        }
        catch (IOException)
        {
            await Emit(new SyncCompletedEvent(filename, false, "Cannot check target drive space \u2014 it may be disconnected"));
            return;
        }

        var dest = Path.Combine(syncPath, filename);
        IsCopying = true;
        CurrentFile = filename;
        CurrentProgress = 0;

        try
        {
            var totalBytes = sourceSize;
            var buffer = new byte[1024 * 256];
            long copied = 0;
            var lastReport = DateTime.UtcNow;

            await using var sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, buffer.Length, true);
            await using var destStream = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, true);

            int bytesRead;
            while ((bytesRead = await sourceStream.ReadAsync(buffer, ct)) > 0)
            {
                await destStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                copied += bytesRead;
                CurrentProgress = totalBytes > 0 ? (double)copied / totalBytes * 100 : 0;

                if ((DateTime.UtcNow - lastReport).TotalSeconds >= 1)
                {
                    lastReport = DateTime.UtcNow;
                    await Emit(new SyncProgressEvent(filename, CurrentProgress, copied, totalBytes));
                }
            }

            CurrentProgress = 100;
            await Emit(new SyncProgressEvent(filename, 100, totalBytes, totalBytes));
            await Emit(new SyncCompletedEvent(filename, true, null));
        }
        catch (OperationCanceledException)
        {
            TryDeletePartial(dest);
            await Emit(new SyncCompletedEvent(filename, false, "Cancelled"));
        }
        catch (IOException ex) when (IsDiskError(ex))
        {
            _log.LogError(ex, "Disk I/O error copying {File}", filename);
            TryDeletePartial(dest);
            await Emit(new SyncCompletedEvent(filename, false,
                "Disk error \u2014 drive may have been disconnected or is full"));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to copy {File} to sync path", filename);
            TryDeletePartial(dest);
            await Emit(new SyncCompletedEvent(filename, false, ex.Message));
        }
        finally
        {
            IsCopying = false;
            CurrentFile = null;
            CurrentProgress = 0;
        }
    }

    public async Task CopyAllNewAsync()
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            var result = Compare();
            if (result.Error is not null)
            {
                await Emit(new SyncCompletedEvent("", false, result.Error));
                return;
            }

            foreach (var file in result.New)
            {
                if (ct.IsCancellationRequested) break;
                await CopyFileAsync(file.Name, ct);
            }
        }
        finally { _cts = null; }
    }

    public void Cancel() => _cts?.Cancel();

    // --- Bridge helper ---

    private Task Emit(SyncEvent evt) => _bridge.SendAsync(evt);

    // --- Helpers ---

    internal static bool IsPathAccessible(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (!string.IsNullOrEmpty(root))
            {
                var drive = new DriveInfo(root);
                if (!drive.IsReady) return false;
            }
            return Directory.Exists(path);
        }
        catch { return false; }
    }

    private static List<FileInfo> SafeGetIsos(string path)
        => Directory.GetFiles(path, "*.iso").Select(f => new FileInfo(f)).ToList();

    private static long SafeLength(FileInfo fi)
    {
        try { return fi.Length; }
        catch { return 0; }
    }

    internal static SyncDiskInfo? GetDiskInfo(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root)) return null;

            var drive = new DriveInfo(root);
            if (!drive.IsReady) return null;

            var isos = Directory.Exists(path)
                ? Directory.GetFiles(path, "*.iso").Select(f =>
                {
                    try { return new FileInfo(f); }
                    catch { return null; }
                }).Where(f => f is not null).ToList()
                : [];

            var label = drive.VolumeLabel is { Length: > 0 } vl
                ? $"{vl} ({drive.Name.TrimEnd(Path.DirectorySeparatorChar)})"
                : drive.Name.TrimEnd(Path.DirectorySeparatorChar);

            return new SyncDiskInfo(label, isos.Count, isos.Sum(f => f!.Length),
                drive.AvailableFreeSpace, drive.TotalSize);
        }
        catch { return null; }
    }

    internal static bool IsDiskError(IOException ex)
    {
        return ex.HResult is
            unchecked((int)0x80070015) or unchecked((int)0x80070070) or
            unchecked((int)0x80070035) or unchecked((int)0x80070033) or
            unchecked((int)0x80004005) or unchecked((int)0x8007001F);
    }

    private static void TryDeletePartial(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    internal static string FormatBytes(long bytes)
    {
        if (bytes >= 1073741824) return $"{bytes / 1073741824.0:F2} GB";
        if (bytes >= 1048576) return $"{bytes / 1048576.0:F2} MB";
        return $"{bytes / 1024.0:F2} KB";
    }
}
