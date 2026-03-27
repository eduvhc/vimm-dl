using Module.Download;
using Module.Ps3Pipeline;
using Module.Ps3IsoTools;

/// <summary>
/// Thin host-level wrapper that wires DownloadService to the repo, PS3 pipeline, and settings.
/// </summary>
class DownloadQueue
{
    private readonly DownloadService _service;
    private readonly QueueRepository _repo;
    private readonly QueueItemProvider _provider;
    private readonly Ps3ConversionPipeline _ps3Pipeline;

    public DownloadQueue(DownloadService service, QueueRepository repo, Ps3ConversionPipeline ps3Pipeline)
    {
        _service = service;
        _repo = repo;
        _provider = new QueueItemProvider(repo);
        _ps3Pipeline = ps3Pipeline;

        _service.OnPostDownload = HandlePostDownload;
    }

    // Delegate to service
    public bool IsRunning => _service.IsRunning;
    public bool IsPaused => _service.IsPaused;
    public string? CurrentFile => _service.CurrentFile;
    public string? CurrentUrl => _service.CurrentUrl;
    public string? CurrentProgress => _service.CurrentProgress;
    public long TotalBytes => _service.TotalBytes;
    public long DownloadedBytes => _service.DownloadedBytes;

    public string GetBasePath() => _service.GetBasePath();
    public void Stop() => _service.Stop();
    public void Pause() => _service.Pause();

    public void Start(string? overridePath)
    {
        _service.Configure(_repo.GetDownloadPath());
        _service.Start(_provider, overridePath);
    }

    private async Task HandlePostDownload(string url, string filename, string completedFilePath, int format)
    {
        var dlMeta = _repo.GetMeta(url);
        if (dlMeta == null || !Module.Core.Platforms.IsPS3(dlMeta.Platform))
            return;

        var serial = dlMeta.Serial;
        var renameOpts = LoadRenameOptions();
        var downloadPath = _service.GetBasePath();

        var completedDir = Path.Combine(downloadPath, "completed");
        var tempBaseDir = Path.Combine(downloadPath, "ps3_temp");

        if (format == 0)
        {
            _ps3Pipeline.JbFolder.Enqueue(completedFilePath, completedDir, tempBaseDir);
        }
        else if (Module.Core.FileExtensions.IsDecIso(filename))
        {
            _ = Task.Run(() => _ps3Pipeline.DecIso.RenameDecIsoAsync(completedFilePath, serial, renameOpts));
        }
        else if (format > 0 && Module.Core.FileExtensions.IsArchive(filename))
        {
            _ = Task.Run(() => _ps3Pipeline.DecIso.ExtractAndRenameDecIsoAsync(
                completedFilePath, completedDir, tempBaseDir, serial, renameOpts));
        }
    }

    private IsoRenameOptions LoadRenameOptions()
    {
        var s = _repo.GetAllSettings();
        return new IsoRenameOptions(
            FixThe: s.GetValueOrDefault(SettingsKeys.FixThe, "true") == "true",
            AddSerial: s.GetValueOrDefault(SettingsKeys.AddSerial, "true") == "true",
            StripRegion: s.GetValueOrDefault(SettingsKeys.StripRegion, "true") == "true"
        );
    }
}
