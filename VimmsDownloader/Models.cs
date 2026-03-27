record SetPathRequest(string Path);
record AddRequest(List<string> Urls, int? Format = null);
record MoveRequest(int Id, string Direction);
record SetFormatRequest(int Id, int Format);

record VersionResponse(string Current, string? Latest, bool HasUpdate, string? Url, string? Changelog);
record DataResponse(List<QueuedItem> Queued, List<HistoryItem> History);
record HistoryItem(int Id, string Url, string Filename, string? Filepath,
    string? Title, string? Platform, string? Size,
    bool FileExists, long? FileSize,
    string? IsoFilename, bool IsoExists, long? IsoSize,
    string? ConvPhase, string? ConvMessage,
    string? CompletedAt);
record QueueListResponse(List<QueueIdRow> Queued);
record QueueIdRow(int Id, string Url, int Format);
record QueuedItem(int Id, string Url, int Format, string? Title, string? Platform, string? Size, string? Formats);
record CompletedItem(int Id, string Url, string Filename, string? Filepath,
    string? CompletedAt = null, string? Title = null, string? Platform = null, string? Size = null);
record MetaResponse(string Title, string Platform, string Size, string? Formats);
record FormatOption(int Value, string Label, string Title, string Size);
record PartialsResponse(string? Path, List<PartialFile> Files);
record PartialFile(string Name, long Bytes, double Mb);
record ConfigResponse(string Platform, string OsDescription, string Hostname, string User,
    string DefaultPath, string ActivePath, bool IsRunning, string? CurrentFile, string? Progress);
record CheckPathResponse(string? Path, bool Exists, bool Writable, long? FreeSpace, string? Error);
record StatusResponse(bool IsRunning, bool IsPaused, string? CurrentFile, string? CurrentUrl,
    string? Progress, long TotalBytes, long DownloadedBytes, List<LogEntry> RecentLogs, List<PartialFile>? Partials);
record LogEntry(string Time, string Type, string Message);
record CompletedEvent(string Url, string Filename, string Filepath);
record ConvertPs3Response(int Queued, int Skipped, List<string> Files);
record ConvertSingleRequest(string Filename);
record ConvertSingleResponse(bool Enqueued, string Filename);
record AbortResponse(bool Aborted);

// Sync (request-only records that live in the web layer)
record SyncCopyRequest(string Filename);
record SyncSetPathRequest(string Path);

static class QueueLock
{
    public static readonly object Sync = new();
}

static class PathHelpers
{
    private static readonly string[] ArchiveExts = [".7z", ".zip", ".rar"];

    public static bool IsArchive(string filename)
        => ArchiveExts.Any(e => filename.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    public static string? ExpandPath(string? p)
    {
        p = p?.Trim();
        if (string.IsNullOrEmpty(p)) return p;
        if (p.StartsWith("~/"))
            p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), p[2..]);
        return p;
    }
}
