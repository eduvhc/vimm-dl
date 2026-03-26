using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();
builder.Services.AddSingleton<DownloadQueue>();
builder.Services.AddHttpClient("vimms")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
        UseCookies = true,
        CookieContainer = new CookieContainer(),
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 10
    })
    .ConfigureHttpClient(c =>
    {
        c.Timeout = TimeSpan.FromMinutes(60);
        c.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        c.DefaultRequestHeaders.Add("Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
        c.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        c.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
        c.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
        c.DefaultRequestHeaders.Add("Pragma", "no-cache");
        c.DefaultRequestHeaders.Add("Sec-CH-UA", "\"Google Chrome\";v=\"131\", \"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"");
        c.DefaultRequestHeaders.Add("Sec-CH-UA-Mobile", "?0");
        c.DefaultRequestHeaders.Add("Sec-CH-UA-Platform", "\"Windows\"");
        c.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
        c.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
        c.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
        c.DefaultRequestHeaders.Add("Sec-Fetch-User", "?1");
        c.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
        c.DefaultRequestHeaders.Add("DNT", "1");
        c.DefaultRequestHeaders.Referrer = new Uri("https://vimm.net/");
    });

var app = builder.Build();

// Configure DB path from config or env var
var dbConnStr = app.Configuration.GetConnectionString("Default");
if (!string.IsNullOrEmpty(dbConnStr))
{
    Db.ConnectionString = dbConnStr;
    // Ensure directory exists for the db file
    var dbPath = dbConnStr.Replace("Data Source=", "").Trim();
    var dbDir = Path.GetDirectoryName(dbPath);
    if (!string.IsNullOrEmpty(dbDir)) Directory.CreateDirectory(dbDir);
}

// Init DB
using (var db = Db.Open())
{
    // WAL mode for safe concurrent reads/writes
    db.Execute("PRAGMA journal_mode=WAL");

    db.Execute("""
        CREATE TABLE IF NOT EXISTS queued_urls (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            url TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS completed_urls (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            url TEXT NOT NULL,
            filename TEXT NOT NULL,
            filepath TEXT
        );
        CREATE TABLE IF NOT EXISTS url_meta (
            url TEXT PRIMARY KEY,
            title TEXT NOT NULL,
            platform TEXT NOT NULL,
            size TEXT NOT NULL
        );
    """);
}

// Auto-resume: if there are queued URLs, start downloading on app launch
{
    using var checkDb = Db.Open();
    var hasQueued = checkDb.QueryFirstOrDefault<int>("SELECT COUNT(*) FROM queued_urls") > 0;
    if (hasQueued)
    {
        _ = Task.Run(async () =>
        {
            // Wait for the app to fully start
            await Task.Delay(1500);
            var queue = app.Services.GetRequiredService<DownloadQueue>();
            if (!queue.IsRunning)
            {
                var dlPath = app.Configuration.GetValue<string>("DownloadPath");
                if (string.IsNullOrWhiteSpace(dlPath))
                    dlPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                queue.Start(dlPath);
            }
        });
    }
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHub<DownloadHub>("/hub");

// --- Data APIs ---

app.MapGet("/api/data", () =>
{
    using var db = Db.Open();
    var queued = db.Query("""
        SELECT q.id, q.url, m.title, m.platform, m.size
        FROM queued_urls q
        LEFT JOIN url_meta m ON q.url = m.url
        ORDER BY q.id
    """).ToList();
    return new
    {
        queued,
        completed = db.Query("SELECT id, url, filename, filepath FROM completed_urls").ToList()
    };
});

app.MapPost("/api/queue", (AddRequest req) =>
{
    using var db = Db.Open();
    foreach (var url in req.Urls.Take(40))
        db.Execute("INSERT INTO queued_urls (url) VALUES (@url)", new { url });
    return Results.Ok(new { queued = db.Query("SELECT id, url FROM queued_urls").ToList() });
});

app.MapDelete("/api/queue/{id:int}", (int id) =>
{
    lock (QueueLock.Sync)
    {
        using var db = Db.Open();
        db.Execute("DELETE FROM queued_urls WHERE id = @id", new { id });
        return Results.Ok();
    }
});

app.MapPost("/api/queue/move", (MoveRequest req) =>
{
    lock (QueueLock.Sync)
    {
        using var db = Db.Open();
        using var tx = db.BeginTransaction();
        try
        {
            var ids = db.Query<int>("SELECT id FROM queued_urls ORDER BY id", transaction: tx).ToList();
            var idx = ids.IndexOf(req.Id);
            if (idx < 0) { tx.Rollback(); return Results.NotFound(); }
            var targetIdx = req.Direction == "up" ? idx - 1 : idx + 1;
            if (targetIdx < 0 || targetIdx >= ids.Count) { tx.Rollback(); return Results.Ok(); }
            var otherId = ids[targetIdx];
            var tempId = -999;
            db.Execute("UPDATE queued_urls SET id = @tempId WHERE id = @id", new { tempId, id = req.Id }, tx);
            db.Execute("UPDATE queued_urls SET id = @newId WHERE id = @otherId", new { newId = req.Id, otherId }, tx);
            db.Execute("UPDATE queued_urls SET id = @newId WHERE id = @tempId", new { newId = otherId, tempId }, tx);
            tx.Commit();
            return Results.Ok();
        }
        catch { tx.Rollback(); return Results.StatusCode(500); }
    }
});

app.MapDelete("/api/queue", () =>
{
    using var db = Db.Open();
    db.Execute("DELETE FROM queued_urls");
    return Results.Ok();
});

// --- Check if file exists in completed ---

app.MapGet("/api/check-exists", (string filename, DownloadQueue queue) =>
{
    var dlPath = queue.ActiveDownloadPath
        ?? app.Configuration.GetValue<string>("DownloadPath");
    if (string.IsNullOrWhiteSpace(dlPath))
        dlPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    // ActiveDownloadPath points to downloading/, go up one level for the base
    var basePath = dlPath.EndsWith("downloading") ? Path.GetDirectoryName(dlPath)! : dlPath;
    var completedPath = Path.Combine(basePath, "completed");
    var filePath = Path.Combine(completedPath, filename);
    var exists = File.Exists(filePath);
    long? size = exists ? new FileInfo(filePath).Length : null;
    return Results.Ok(new { exists, size, path = filePath });
});

// --- Partial file check ---

app.MapGet("/api/partials", (DownloadQueue queue) =>
{
    var dlPath = queue.ActiveDownloadPath
        ?? app.Configuration.GetValue<string>("DownloadPath");
    if (string.IsNullOrWhiteSpace(dlPath))
        dlPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    if (!Directory.Exists(dlPath))
        return Results.Ok(new { files = new List<object>() });

    var files = Directory.GetFiles(dlPath)
        .Select(f => new FileInfo(f))
        .Where(f => f.Length > 0)
        .Select(f => new { name = f.Name, bytes = f.Length, mb = Math.Round(f.Length / 1048576.0, 2) })
        .ToList();

    return Results.Ok(new { path = dlPath, files });
});

// --- Metadata API ---

app.MapGet("/api/meta", async (string url, IHttpClientFactory httpFactory) =>
{
    // Check DB cache first
    using var db = Db.Open();
    var cached = db.QueryFirstOrDefault<(string Title, string Platform, string Size)>(
        "SELECT title, platform, size FROM url_meta WHERE url = @url", new { url });
    if (cached != default && !string.IsNullOrEmpty(cached.Title))
    {
        // Decode in case old entries had HTML entities
        var ct = System.Net.WebUtility.HtmlDecode(cached.Title);
        var cp = System.Net.WebUtility.HtmlDecode(cached.Platform);
        // If it changed, update the DB
        if (ct != cached.Title || cp != cached.Platform)
            db.Execute("UPDATE url_meta SET title=@t, platform=@p WHERE url=@url", new { t = ct, p = cp, url });
        return Results.Ok(new { title = ct, platform = cp, size = cached.Size });
    }

    try
    {
        var http = httpFactory.CreateClient("vimms");
        var html = await http.GetStringAsync(url);

        // Title: <title>The Vault: Game Name (System)</title>
        var titleMatch = Regex.Match(html, @"<title>(?:The Vault:\s*)?(.+?)\s*</title>", RegexOptions.IgnoreCase);
        var fullTitle = System.Net.WebUtility.HtmlDecode(
            titleMatch.Success ? titleMatch.Groups[1].Value.Trim() : "Unknown");

        // Platform: <div class="sectionTitle">PlayStation 3</div> or <h2>
        var platformMatch = Regex.Match(html, @"class=""sectionTitle""[^>]*>\s*([^<]+?)\s*</div>", RegexOptions.IgnoreCase);
        if (!platformMatch.Success)
            platformMatch = Regex.Match(html, @"<h2[^>]*>\s*([^<]+?)\s*</h2>", RegexOptions.IgnoreCase);
        var platform = System.Net.WebUtility.HtmlDecode(
            platformMatch.Success ? platformMatch.Groups[1].Value.Trim() : "");

        // Game title without platform suffix
        var title = Regex.Replace(fullTitle, @"\s*\([^)]*\)\s*$", "").Trim();
        if (string.IsNullOrEmpty(title)) title = fullTitle;

        // Size: look for pattern like "13.4 GB" or "335 MB" near the download button
        var sizeMatch = Regex.Match(html, @"([\d,.]+)\s*(GB|MB|KB)", RegexOptions.IgnoreCase);
        var size = sizeMatch.Success ? $"{sizeMatch.Groups[1].Value} {sizeMatch.Groups[2].Value}" : "";

        // Store in DB
        db.Execute("""
            INSERT OR REPLACE INTO url_meta (url, title, platform, size)
            VALUES (@url, @title, @platform, @size)
        """, new { url, title, platform, size });

        return Results.Ok(new { title, platform, size });
    }
    catch
    {
        return Results.Ok(new { title = url.Split('/').Last(), platform = "", size = "" });
    }
});

// --- Config APIs ---

app.MapGet("/api/config", (DownloadQueue queue) =>
{
    var isWindows = OperatingSystem.IsWindows();
    var isLinux = OperatingSystem.IsLinux();
    var isMac = OperatingSystem.IsMacOS();
    var platform = isWindows ? "windows" : isLinux ? "linux" : isMac ? "macos" : "unknown";
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    var defaultPath = Path.Combine(home, "Downloads");

    var configuredPath = app.Configuration.GetValue<string>("DownloadPath");
    var activePath = string.IsNullOrWhiteSpace(configuredPath) ? defaultPath : configuredPath;

    return new
    {
        platform,
        osDescription = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        hostname = Environment.MachineName,
        user = Environment.UserName,
        defaultPath,
        activePath,
        isRunning = queue.IsRunning,
        currentFile = queue.CurrentFile,
        progress = queue.CurrentProgress
    };
});

app.MapPost("/api/config/check-path", (SetPathRequest req) =>
{
    var path = ExpandPath(req.Path);
    if (string.IsNullOrEmpty(path))
        return Results.Ok(new { exists = false, writable = false, error = "empty path" });

    var exists = Directory.Exists(path);
    var writable = false;
    string? error = null;
    long? freeSpace = null;

    if (exists)
    {
        try
        {
            // Test write access
            writable = true;
            try
            {
                var testFile = Path.Combine(path, $".vimms_wt_{Guid.NewGuid():N}");
                using (File.Create(testFile)) { }
                File.Delete(testFile);
            }
            catch { writable = false; }
            var driveInfo = new DriveInfo(Path.GetPathRoot(path)!);
            freeSpace = driveInfo.AvailableFreeSpace;
        }
        catch (Exception ex) { error = ex.Message; }
    }
    else
    {
        error = "Directory does not exist";
    }

    return Results.Ok(new { path, exists, writable, freeSpace, error });
});

// --- Status API (for reconnecting clients) ---

app.MapGet("/api/status", (DownloadQueue queue) =>
{
    // Check for partial downloads in the download path
    var dlPath = queue.ActiveDownloadPath
        ?? app.Configuration.GetValue<string>("DownloadPath");
    if (string.IsNullOrWhiteSpace(dlPath))
        dlPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    List<object>? partials = null;
    using var db = Db.Open();
    var queuedUrls = db.Query<(int Id, string Url)>("SELECT id, url FROM queued_urls").ToList();
    if (queuedUrls.Count > 0 && !queue.IsRunning && Directory.Exists(dlPath))
    {
        // Look for any partial files that match queued items
        partials = [];
        foreach (var file in Directory.GetFiles(dlPath))
        {
            var fi = new FileInfo(file);
            if (fi.Length > 0)
                partials.Add(new { name = fi.Name, size = fi.Length, sizeMB = fi.Length / 1048576.0 });
        }
    }

    return new
    {
        isRunning = queue.IsRunning,
        isPaused = queue.IsPaused,
        currentFile = queue.CurrentFile,
        currentUrl = queue.CurrentUrl,
        progress = queue.CurrentProgress,
        totalBytes = queue.TotalBytes,
        downloadedBytes = queue.DownloadedBytes,
        recentLogs = queue.GetRecentLogs(),
        partials
    };
});

app.Run();

// --- Helpers ---

static string? ExpandPath(string? p)
{
    p = p?.Trim();
    if (string.IsNullOrEmpty(p)) return p;
    if (p.StartsWith("~/"))
        p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), p[2..]);
    return p;
}

record SetPathRequest(string Path);
record AddRequest(List<string> Urls);
record MoveRequest(int Id, string Direction);

// Shared lock for queue mutations (move, delete, complete)
static class QueueLock
{
    public static readonly object Sync = new();
}

static class Db
{
    public static string ConnectionString { get; set; } = "Data Source=queue.db";

    public static SqliteConnection Open()
    {
        var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        return conn;
    }
}

// --- SignalR Hub ---

public class DownloadHub : Hub
{
    private readonly DownloadQueue _queue;
    public DownloadHub(DownloadQueue queue) => _queue = queue;

    public async Task StartDownload(string? downloadPath)
    {
        if (_queue.IsRunning)
        {
            // Pause current download first, then restart (resume picks up partial files)
            _queue.Pause();
            // Wait for it to actually stop
            var timeout = DateTime.UtcNow.AddSeconds(10);
            while (_queue.IsRunning && DateTime.UtcNow < timeout)
                await Task.Delay(200);
        }
        _queue.Start(downloadPath);
    }

    public async Task StartSpecific(string? downloadPath, int queueId)
    {
        // Move the selected item to front of queue
        using var db = Db.Open();
        var minId = db.QueryFirstOrDefault<int?>("SELECT MIN(id) FROM queued_urls");
        if (minId.HasValue && queueId != minId.Value)
        {
            // Swap: give selected item the lowest id
            var newId = minId.Value - 1;
            db.Execute("UPDATE queued_urls SET id = @newId WHERE id = @queueId", new { newId, queueId });
        }

        if (_queue.IsRunning)
        {
            _queue.Pause();
            var timeout = DateTime.UtcNow.AddSeconds(10);
            while (_queue.IsRunning && DateTime.UtcNow < timeout)
                await Task.Delay(200);
        }
        _queue.Start(downloadPath);
    }

    public void PauseDownload() => _queue.Pause();
    public void StopDownload() => _queue.Stop();
}

// --- Download Queue Service (decoupled from SignalR connection) ---

public class DownloadQueue
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<DownloadQueue> _log;
    private readonly IHubContext<DownloadHub> _hub;
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

    public DownloadQueue(IHttpClientFactory httpFactory, IConfiguration config, ILogger<DownloadQueue> log, IHubContext<DownloadHub> hub)
    {
        _httpFactory = httpFactory;
        _config = config;
        _log = log;
        _hub = hub;
    }

    public record LogEntry(string Time, string Type, string Message);

    public List<LogEntry> GetRecentLogs() => _recentLogs.ToList();

    private async Task Emit(string evt, string msg)
    {
        _recentLogs.Enqueue(new LogEntry(DateTime.Now.ToString("HH:mm:ss"), evt, msg));
        while (_recentLogs.Count > 200) _recentLogs.TryDequeue(out _);
        try { await _hub.Clients.All.SendAsync(evt, msg); } catch { }
    }

    private async Task EmitObj(string evt, object data)
    {
        try { await _hub.Clients.All.SendAsync(evt, data); } catch { }
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
                using var db = Db.Open();
                var row = db.QueryFirstOrDefault<(int Id, string Url)>("SELECT id, url FROM queued_urls ORDER BY id LIMIT 1");
                if (row == default) break;

                var (id, url) = row;
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
                        db.Execute("DELETE FROM queued_urls WHERE id = @id", new { id });
                        continue;
                    }

                    var mediaId = mediaIdMatch.Groups[1].Value;

                    var titleMatch = Regex.Match(pageHtml, @"<title>(?:The Vault:\s*)?(.+?)\s*</title>", RegexOptions.IgnoreCase);
                    var gameTitle = titleMatch.Success ? titleMatch.Groups[1].Value.Trim() : "download";

                    // Find download server URL from:
                    // 1. form action attribute
                    // 2. JS-assigned action (e.g. dl_form.action='//dl3.vimm.net/')
                    // 3. Known dl server pattern in page source
                    string? dlServer = null;

                    // Try form action attribute
                    var actionMatch = Regex.Match(pageHtml, @"id=""dl_form""[^>]*action=""([^""]+)""", RegexOptions.IgnoreCase);
                    if (!actionMatch.Success)
                        actionMatch = Regex.Match(pageHtml, @"action=""([^""]+)""[^>]*id=""dl_form""", RegexOptions.IgnoreCase);
                    if (actionMatch.Success)
                        dlServer = actionMatch.Groups[1].Value;

                    // Try JS-assigned action (common pattern on Vimm's)
                    if (dlServer == null)
                    {
                        var jsAction = Regex.Match(pageHtml, @"\.action\s*=\s*['""]([^'""]+)['""]", RegexOptions.IgnoreCase);
                        if (jsAction.Success)
                            dlServer = jsAction.Groups[1].Value;
                    }

                    // Try finding dl server URL anywhere in page
                    if (dlServer == null)
                    {
                        var dlMatch = Regex.Match(pageHtml, @"(https?://dl\d*\.vimm\.net/?)", RegexOptions.IgnoreCase);
                        if (dlMatch.Success)
                            dlServer = dlMatch.Groups[1].Value;
                    }

                    // Try protocol-relative
                    if (dlServer == null)
                    {
                        var prMatch = Regex.Match(pageHtml, @"(//dl\d*\.vimm\.net/?)", RegexOptions.IgnoreCase);
                        if (prMatch.Success)
                            dlServer = "https:" + prMatch.Groups[1].Value;
                    }

                    dlServer ??= "https://dl3.vimm.net/";

                    // Resolve to absolute URL
                    var pageUri = new Uri(url);
                    Uri dlBaseUri;
                    if (dlServer.StartsWith("//"))
                        dlBaseUri = new Uri($"https:{dlServer}");
                    else if (Uri.TryCreate(dlServer, UriKind.Absolute, out var abs) && (abs.Scheme == "http" || abs.Scheme == "https"))
                        dlBaseUri = abs;
                    else
                        dlBaseUri = new Uri(pageUri, dlServer);

                    // Force https
                    if (dlBaseUri.Scheme == "file" || dlBaseUri.Scheme != "https")
                    {
                        var fixedUrl = $"https://{dlBaseUri.Host}{dlBaseUri.AbsolutePath}";
                        dlBaseUri = new Uri(fixedUrl);
                    }

                    var downloadUrl = $"{dlBaseUri.GetLeftPart(UriPartial.Path).TrimEnd('/')}/?mediaId={mediaId}&alt=0";
                    await Emit("Status", $"Download URL: {downloadUrl}");
                    await Emit("Status", $"Downloading: {gameTitle} (mediaId={mediaId})");

                    // First request without Range to get filename and total size
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

                    // Check if partial file exists for resume
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
                            if (File.Exists(completedFilePath))
                                File.Delete(completedFilePath);
                            File.Move(filePath, completedFilePath);

                            using var tx = db.BeginTransaction();
                            db.Execute("DELETE FROM queued_urls WHERE id = @id", new { id }, tx);
                            db.Execute("INSERT INTO completed_urls (url, filename, filepath) VALUES (@url, @filename, @filepath)",
                                new { url, filename, filepath = completedFilePath }, tx);
                            tx.Commit();
                        }

                        await EmitObj("Completed", new { url, filename, filepath = completedFilePath });
                        _log.LogInformation("Recovered completed file: {Filename}", filename);
                        continue;
                    }

                    HttpResponseMessage response;
                    bool resumed = false;

                    if (existingBytes > 0 && existingBytes < totalBytes)
                    {
                        // Dispose the first response and make a Range request
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
                            // Server doesn't support Range, start over
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

                    if (!resumed) response.Dispose();

                    // Move from downloading/ to completed/ (locked + transactional)
                    lock (QueueLock.Sync)
                    {
                        if (File.Exists(completedFilePath))
                            File.Delete(completedFilePath);
                        File.Move(filePath, completedFilePath);

                        using var tx = db.BeginTransaction();
                        try
                        {
                            db.Execute("DELETE FROM queued_urls WHERE id = @id", new { id }, tx);
                            db.Execute("INSERT INTO completed_urls (url, filename, filepath) VALUES (@url, @filename, @filepath)",
                                new { url, filename, filepath = completedFilePath }, tx);
                            tx.Commit();
                        }
                        catch
                        {
                            tx.Rollback();
                            // File already moved, move it back to avoid orphan
                            if (File.Exists(completedFilePath) && !File.Exists(filePath))
                                File.Move(completedFilePath, filePath);
                            throw;
                        }
                    }

                    await EmitObj("Completed", new { url, filename, filepath = completedFilePath });
                    _log.LogInformation("Downloaded {Filename} -> completed/", filename);

                    var delay = rand.Next(5, 31);
                    await Emit("Status", $"Waiting {delay}s before next download...");
                    await Task.Delay(delay * 1000, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Error downloading {Url}", url);
                    await Emit("Error", $"Failed: {url} - {ex.Message}");
                    using var db2 = Db.Open();
                    db2.Execute("DELETE FROM queued_urls WHERE id = @id", new { id });
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
