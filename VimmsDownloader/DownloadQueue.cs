using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR;

class DownloadQueue
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<DownloadQueue> _log;
    private readonly IHubContext<DownloadHub> _hub;
    private readonly QueueRepository _repo;
    private readonly Ps3ConversionPipeline _ps3Pipeline;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentQueue<LogEntry> _recentLogs = new();

    public bool IsRunning { get; private set; }
    public bool IsPaused { get; private set; }
    public string? CurrentFile { get; private set; }
    public string? CurrentUrl { get; private set; }
    public string? CurrentProgress { get; private set; }
    public long TotalBytes { get; private set; }
    public long DownloadedBytes { get; private set; }
    public string? ActiveDownloadPath { get; private set; }

    public DownloadQueue(IHttpClientFactory httpFactory, IConfiguration config,
        ILogger<DownloadQueue> log, IHubContext<DownloadHub> hub, QueueRepository repo,
        Ps3ConversionPipeline ps3Pipeline)
    {
        _httpFactory = httpFactory;
        _config = config;
        _log = log;
        _hub = hub;
        _repo = repo;
        _ps3Pipeline = ps3Pipeline;
    }

    public string GetBasePath()
    {
        if (!string.IsNullOrEmpty(ActiveDownloadPath))
        {
            var trimmed = ActiveDownloadPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return trimmed.EndsWith("downloading") ? Path.GetDirectoryName(ActiveDownloadPath)! : ActiveDownloadPath;
        }

        return _config.GetValue<string>("DownloadPath")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    public List<LogEntry> GetRecentLogs() => _recentLogs.ToList();

    private async Task Emit(string evt, string msg)
    {
        _recentLogs.Enqueue(new LogEntry(DateTime.Now.ToString("HH:mm:ss"), evt, msg));
        while (_recentLogs.Count > 200) _recentLogs.TryDequeue(out _);
        try { await _hub.Clients.All.SendAsync(evt, msg); } catch { }
    }

    private async Task EmitCompleted(CompletedEvent data)
    {
        try { await _hub.Clients.All.SendAsync("Completed", data); } catch { }
    }

    public void Stop() { IsPaused = false; _cts?.Cancel(); }
    public void Pause() { IsPaused = true; _cts?.Cancel(); }

    public void Start(string? overridePath)
    {
        if (IsRunning) return;
        _ = Task.Run(() => Run(overridePath));
    }

    private async Task Run(string? overridePath)
    {
        IsRunning = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var rand = new Random();
        var downloadPath = !string.IsNullOrWhiteSpace(overridePath) ? overridePath
            : _config.GetValue("DownloadPath",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"))!;

        var downloadingPath = Path.Combine(downloadPath, "downloading");
        var completedPath = Path.Combine(downloadPath, "completed");
        Directory.CreateDirectory(downloadingPath);
        Directory.CreateDirectory(completedPath);
        ActiveDownloadPath = downloadingPath;
        await Emit("Status", $"Download path: {downloadPath}");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var row = _repo.GetNextQueueItem();
                if (row == null) break;

                var (id, url, format) = row.Value;
                CurrentFile = url;
                CurrentUrl = url;
                CurrentProgress = "starting";
                await Emit("Status", $"Processing: {url}");

                try
                {
                    var http = _httpFactory.CreateClient("vimms");

                    var pageHtml = await http.GetStringAsync(url, ct);
                    var mediaIdMatch = Regex.Match(pageHtml, @"name=""mediaId""\s+value=""(\d+)""", RegexOptions.IgnoreCase);
                    if (!mediaIdMatch.Success)
                        mediaIdMatch = Regex.Match(pageHtml, @"value=""(\d+)""\s+name=""mediaId""", RegexOptions.IgnoreCase);

                    if (!mediaIdMatch.Success)
                    {
                        await Emit("Error", $"Could not find mediaId for {url}");
                        _repo.DeleteFromQueue(id);
                        continue;
                    }

                    var mediaId = mediaIdMatch.Groups[1].Value;

                    var titleMatch = Regex.Match(pageHtml, @"<title>(?:The Vault:\s*)?(.+?)\s*</title>", RegexOptions.IgnoreCase);
                    var gameTitle = titleMatch.Success ? titleMatch.Groups[1].Value.Trim() : "download";

                    // Find download server URL
                    string? dlServer = null;

                    var actionMatch = Regex.Match(pageHtml, @"id=""dl_form""[^>]*action=""([^""]+)""", RegexOptions.IgnoreCase);
                    if (!actionMatch.Success)
                        actionMatch = Regex.Match(pageHtml, @"action=""([^""]+)""[^>]*id=""dl_form""", RegexOptions.IgnoreCase);
                    if (actionMatch.Success)
                        dlServer = actionMatch.Groups[1].Value;

                    if (dlServer == null)
                    {
                        var jsAction = Regex.Match(pageHtml, @"\.action\s*=\s*['""]([^'""]+)['""]", RegexOptions.IgnoreCase);
                        if (jsAction.Success) dlServer = jsAction.Groups[1].Value;
                    }

                    if (dlServer == null)
                    {
                        var dlMatch = Regex.Match(pageHtml, @"(https?://dl\d*\.vimm\.net/?)", RegexOptions.IgnoreCase);
                        if (dlMatch.Success) dlServer = dlMatch.Groups[1].Value;
                    }

                    if (dlServer == null)
                    {
                        var prMatch = Regex.Match(pageHtml, @"(//dl\d*\.vimm\.net/?)", RegexOptions.IgnoreCase);
                        if (prMatch.Success) dlServer = "https:" + prMatch.Groups[1].Value;
                    }

                    dlServer ??= "https://dl3.vimm.net/";

                    var pageUri = new Uri(url);
                    Uri dlBaseUri;
                    if (dlServer.StartsWith("//"))
                        dlBaseUri = new Uri($"https:{dlServer}");
                    else if (Uri.TryCreate(dlServer, UriKind.Absolute, out var abs) && (abs.Scheme == "http" || abs.Scheme == "https"))
                        dlBaseUri = abs;
                    else
                        dlBaseUri = new Uri(pageUri, dlServer);

                    if (dlBaseUri.Scheme == "file" || dlBaseUri.Scheme != "https")
                    {
                        var fixedUrl = $"https://{dlBaseUri.Host}{dlBaseUri.AbsolutePath}";
                        dlBaseUri = new Uri(fixedUrl);
                    }

                    var downloadUrl = format > 0
                        ? $"{dlBaseUri.GetLeftPart(UriPartial.Path).TrimEnd('/')}/?mediaId={mediaId}&alt={format}"
                        : $"{dlBaseUri.GetLeftPart(UriPartial.Path).TrimEnd('/')}/?mediaId={mediaId}";
                    await Emit("Status", $"Download URL: {downloadUrl}");
                    await Emit("Status", $"Downloading: {gameTitle} (mediaId={mediaId})");

                    var headRequest = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                    headRequest.Headers.Referrer = new Uri(url);
                    headRequest.Headers.Add("Sec-Fetch-Site", "cross-site");
                    using var headResponse = await http.SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead, ct);
                    headResponse.EnsureSuccessStatusCode();

                    var filename = headResponse.Content.Headers.ContentDisposition?.FileNameStar
                        ?? headResponse.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                        ?? $"{gameTitle}.zip";
                    filename = string.Join("_", filename.Split(Path.GetInvalidFileNameChars()));

                    CurrentFile = filename;
                    var filePath = Path.Combine(downloadingPath, filename);
                    var completedFilePath = Path.Combine(completedPath, filename);
                    var totalBytes = headResponse.Content.Headers.ContentLength ?? 0;
                    TotalBytes = totalBytes;

                    long existingBytes = 0;
                    if (File.Exists(filePath))
                        existingBytes = new FileInfo(filePath).Length;

                    // File already fully downloaded (app killed before move)
                    if (existingBytes > 0 && totalBytes > 0 && existingBytes >= totalBytes)
                    {
                        headResponse.Dispose();
                        await Emit("Status", $"Already downloaded: {filename}, moving to completed");

                        lock (QueueLock.Sync)
                        {
                            if (File.Exists(completedFilePath)) File.Delete(completedFilePath);
                            File.Move(filePath, completedFilePath);
                            _repo.CompleteItem(id, url, filename, completedFilePath);
                        }

                        await EmitCompleted(new CompletedEvent(url, filename, completedFilePath));
                        _log.LogInformation("Recovered completed file: {Filename}", filename);
                        continue;
                    }

                    HttpResponseMessage response;
                    bool resumed = false;

                    if (existingBytes > 0 && existingBytes < totalBytes)
                    {
                        headResponse.Dispose();
                        var rangeRequest = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                        rangeRequest.Headers.Referrer = new Uri(url);
                        rangeRequest.Headers.Add("Sec-Fetch-Site", "cross-site");
                        rangeRequest.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingBytes, null);
                        response = await http.SendAsync(rangeRequest, HttpCompletionOption.ResponseHeadersRead, ct);

                        if (response.StatusCode == System.Net.HttpStatusCode.PartialContent)
                        {
                            resumed = true;
                            await Emit("Status", $"Resuming {filename} from {existingBytes / 1048576.0:F2} MB");
                        }
                        else
                        {
                            response.Dispose();
                            existingBytes = 0;
                            var freshRequest = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                            freshRequest.Headers.Referrer = new Uri(url);
                            freshRequest.Headers.Add("Sec-Fetch-Site", "cross-site");
                            response = await http.SendAsync(freshRequest, HttpCompletionOption.ResponseHeadersRead, ct);
                            response.EnsureSuccessStatusCode();
                        }
                    }
                    else
                    {
                        existingBytes = 0;
                        response = headResponse;
                    }

                    await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
                    await using var fileStream = new FileStream(filePath,
                        resumed ? FileMode.Append : FileMode.Create,
                        FileAccess.Write, FileShare.None, 81920);

                    var buffer = new byte[81920];
                    long downloaded = existingBytes;
                    int bytesRead;
                    var lastReport = DateTime.UtcNow;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                        downloaded += bytesRead;
                        DownloadedBytes = downloaded;

                        if ((DateTime.UtcNow - lastReport).TotalSeconds >= 2)
                        {
                            var pct = totalBytes > 0 ? (double)downloaded * 100 / totalBytes : -1;
                            var mb = downloaded / 1048576.0;
                            var totalMb = totalBytes > 0 ? totalBytes / 1048576.0 : 0;
                            CurrentProgress = totalBytes > 0
                                ? $"{filename}: {mb:F2} / {totalMb:F2} MB ({pct:F2}%)"
                                : $"{filename}: {mb:F2} MB downloaded";
                            await Emit("Progress", CurrentProgress);
                            lastReport = DateTime.UtcNow;
                        }
                    }

                    // Close file handle before move so extraction worker can access it
                    await fileStream.DisposeAsync();
                    await contentStream.DisposeAsync();
                    if (!resumed) response.Dispose();

                    // Move from downloading/ to completed/ (locked + transactional)
                    lock (QueueLock.Sync)
                    {
                        if (File.Exists(completedFilePath)) File.Delete(completedFilePath);
                        File.Move(filePath, completedFilePath);

                        try
                        {
                            _repo.CompleteItem(id, url, filename, completedFilePath);
                        }
                        catch
                        {
                            if (File.Exists(completedFilePath) && !File.Exists(filePath))
                                File.Move(completedFilePath, filePath);
                            throw;
                        }
                    }

                    await EmitCompleted(new CompletedEvent(url, filename, completedFilePath));
                    _log.LogInformation("Downloaded {Filename} -> completed/", filename);

                    // PS3 JB Folder → ISO conversion (async pipeline, doesn't block next download)
                    if (format == 0)
                    {
                        var meta = _repo.GetMeta(url);
                        if (meta != null && meta.Platform.Equals("PlayStation 3", StringComparison.OrdinalIgnoreCase))
                        {
                            var tempBaseDir = Path.Combine(downloadPath, "ps3_temp");
                            _ps3Pipeline.Enqueue(completedFilePath, completedPath, tempBaseDir);
                        }
                    }

                    var delay = rand.Next(5, 31);
                    await Emit("Status", $"Waiting {delay}s before next download...");
                    await Task.Delay(delay * 1000, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    _log.LogWarning("Rate limited on {Url}, backing off 60s", url);
                    await Emit("Error", $"Rate limited: {url} - waiting 60s before retry");
                    await Task.Delay(60_000, ct);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Error downloading {Url}", url);
                    await Emit("Error", $"Failed: {url} - {ex.Message}");
                    var backoff = rand.Next(15, 46);
                    await Emit("Status", $"Waiting {backoff}s before retry...");
                    await Task.Delay(backoff * 1000, ct);
                }
            }

            await Emit("Done", "All downloads finished.");
        }
        catch (OperationCanceledException)
        {
            if (IsPaused)
                await Emit("Status", "Downloads paused. Resume to continue.");
            else
                await Emit("Status", "Downloads stopped.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Queue processing failed");
            await Emit("Error", $"Queue failed: {ex.Message}");
        }
        finally
        {
            IsRunning = false;
            if (!IsPaused)
            {
                CurrentFile = null;
                CurrentUrl = null;
                CurrentProgress = null;
                TotalBytes = 0;
                DownloadedBytes = 0;
            }
            _cts?.Dispose();
            _cts = null;
        }
    }
}
