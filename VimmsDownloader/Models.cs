record AddRequest(List<string> Urls, int? Format = null);
record QueuePatchRequest(string? Direction = null, int? Format = null);
record QueueReorderRequest(List<int> Ids);

record VersionResponse(string Current, string? Latest, bool HasUpdate, string? Url, string? Changelog);
record DataResponse(List<QueuedItem> Queued, List<HistoryItem> History,
    bool IsRunning, bool IsPaused, string? CurrentFile, string? CurrentUrl,
    string? Progress, long TotalBytes, long DownloadedBytes);
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
record MetaResponse(string Title, string Platform, string Size, string? Formats, string? Serial);
record FormatOption(int Value, string Label, string Title, string Size);
record PartialFile(string Name, long Bytes, double Mb);
record LogEntry(string Time, string Type, string Message);
record CompletedEvent(string Url, string Filename, string Filepath);

record SettingsResponse(string Platform, string OsDescription, string Hostname, string User,
    string DefaultPath, string ActivePath,
    bool FixThe, bool AddSerial, bool StripRegion, int Ps3Parallelism);
record SettingRequest(string Key, string Value);
record CheckPathResponse(string? Path, bool Exists, bool Writable, long? FreeSpace, string? Error);

record QueueExportItem(string Url, int Format);
record QueueImportResponse(int Added, int Skipped);

record Ps3ConvertRequest(string? Filename = null);
record Ps3ConvertResponse(int Queued, int Skipped, List<string> Files);
record Ps3ActionRequest(string Filename, string Action);
record Ps3ActionResponse(bool Success);

record SyncCompareRequest(string Path);
record SyncCopyRequest(string? Filename = null);

static class QueueLock
{
    public static readonly object Sync = new();
}

static class PathHelpers
{
    public static bool IsArchive(string filename)
        => Module.Core.FileExtensions.IsArchive(filename);

    public static string? ExpandPath(string? p)
    {
        p = p?.Trim();
        if (string.IsNullOrEmpty(p)) return p;
        if (p.StartsWith("~/"))
            p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), p[2..]);
        return p;
    }
}
