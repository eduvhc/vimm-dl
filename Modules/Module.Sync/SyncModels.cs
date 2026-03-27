namespace Module.Sync;

public record SyncCompareResponse(string SyncPath, bool PathExists,
    List<SyncFileInfo> New, List<SyncFileInfo> Synced, List<SyncFileInfo> TargetOnly,
    SyncDiskInfo? Source, SyncDiskInfo? Target, string? Error);

public record SyncDiskInfo(string Label, int IsoCount, long IsoTotalSize, long FreeSpace, long TotalSpace);
public record SyncFileInfo(string Name, long Size);
