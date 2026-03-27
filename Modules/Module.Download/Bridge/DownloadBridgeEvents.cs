namespace Module.Download.Bridge;

public abstract record DownloadEvent;

public sealed record DownloadStatusEvent(string Message) : DownloadEvent;
public sealed record DownloadProgressEvent(string Filename, string Progress,
    double Pct, double SpeedMBps, long Downloaded, long Total) : DownloadEvent;
public sealed record DownloadCompletedEvent(string Url, string Filename, string Filepath) : DownloadEvent;
public sealed record DownloadErrorEvent(string Message) : DownloadEvent;
public sealed record DownloadDoneEvent() : DownloadEvent;
