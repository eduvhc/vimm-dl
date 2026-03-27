using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Ps3IsoTools;
using ZipExtractor;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR()
    .AddJsonProtocol(o =>
        o.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));
builder.Services.AddSingleton<QueueRepository>();
builder.Services.AddSingleton<Ps3ConversionPipeline>();
builder.Services.AddSingleton<DownloadQueue>();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));
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

// Init DB
var repo = app.Services.GetRequiredService<QueueRepository>();
repo.Init(app.Configuration.GetConnectionString("Default"));

// Clean up orphaned temp files from previous crashes
{
    var dlBase = app.Configuration.GetValue<string>("DownloadPath")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    app.Services.GetRequiredService<Ps3ConversionPipeline>().CleanupOrphans(dlBase);
}

// Auto-resume: if there are queued URLs, start downloading on app launch
if (repo.HasQueuedUrls())
{
    _ = Task.Run(async () =>
    {
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

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHub<DownloadHub>("/hub");

var currentVersion = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

app.MapGet("/api/version", async (IHttpClientFactory httpFactory) =>
{
    string? latest = null;
    string? url = null;
    string? changelog = null;

    try
    {
        var http = httpFactory.CreateClient();
        http.DefaultRequestHeaders.Add("User-Agent", "vimm-dl");
        var resp = await http.GetAsync("https://api.github.com/repos/eduvhc/vimm-dl/releases/latest");
        if (resp.IsSuccessStatusCode)
        {
            var json = await resp.Content.ReadFromJsonAsync(AppJsonContext.Default.JsonElement);
            latest = json.GetProperty("tag_name").GetString()?.TrimStart('v');
            url = json.GetProperty("html_url").GetString();
            changelog = json.GetProperty("body").GetString();
        }
    }
    catch { }

    var hasUpdate = latest != null && latest != currentVersion &&
        Version.TryParse(latest, out var lv) && Version.TryParse(currentVersion, out var cv) && lv > cv;

    return new VersionResponse(currentVersion, latest, hasUpdate, url, changelog);
});

// --- Data APIs ---

app.MapGet("/api/data", (QueueRepository repo, DownloadQueue queue) =>
{
    var dbItems = repo.GetCompletedItems();
    // Filter DB items to archives only (skip ISOs downloaded as .dec.iso format)
    dbItems.RemoveAll(i =>
    {
        var ext = Path.GetExtension(i.Filename);
        return !ext.Equals(".7z", StringComparison.OrdinalIgnoreCase) &&
               !ext.Equals(".zip", StringComparison.OrdinalIgnoreCase) &&
               !ext.Equals(".rar", StringComparison.OrdinalIgnoreCase);
    });
    var dbFilenames = new HashSet<string>(dbItems.Select(i => i.Filename), StringComparer.OrdinalIgnoreCase);

    // Merge files on disk that aren't in the DB (externally placed or from a previous DB)
    var completedDir = Path.Combine(GetDownloadBasePath(queue), "completed");
    if (Directory.Exists(completedDir))
    {
        var nextId = dbItems.Count > 0 ? dbItems.Max(i => i.Id) + 1 : 1;
        foreach (var file in Directory.GetFiles(completedDir))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith('.')) continue;
            var ext = Path.GetExtension(name);
            if (!ext.Equals(".7z", StringComparison.OrdinalIgnoreCase) &&
                !ext.Equals(".zip", StringComparison.OrdinalIgnoreCase) &&
                !ext.Equals(".rar", StringComparison.OrdinalIgnoreCase)) continue;
            if (!dbFilenames.Contains(name))
                dbItems.Add(new CompletedItem(nextId++, "", name, file));
        }
    }

    return new DataResponse(repo.GetQueuedItems(), dbItems);
});

app.MapPost("/api/queue", (AddRequest req, QueueRepository repo) =>
{
    foreach (var url in req.Urls.Take(40))
        repo.AddToQueue(url, req.Format ?? 0);
    return Results.Ok(new QueueListResponse(repo.GetQueueIds()));
});

app.MapDelete("/api/queue/{id:int}", (int id, QueueRepository repo) =>
{
    lock (QueueLock.Sync)
    {
        repo.DeleteFromQueue(id);
        return Results.Ok();
    }
});

app.MapPost("/api/queue/move", (MoveRequest req, QueueRepository repo) =>
{
    lock (QueueLock.Sync)
        return repo.MoveInQueue(req.Id, req.Direction) ? Results.Ok() : Results.NotFound();
});

app.MapDelete("/api/queue", (QueueRepository repo) =>
{
    repo.ClearQueue();
    return Results.Ok();
});

app.MapDelete("/api/completed/{id:int}", (int id, QueueRepository repo) =>
{
    repo.DeleteCompleted(id);
    return Results.Ok();
});

app.MapPost("/api/queue/format", (SetFormatRequest req, QueueRepository repo) =>
{
    repo.SetFormat(req.Id, req.Format);
    return Results.Ok();
});

// --- Check if file exists in completed ---

app.MapGet("/api/check-exists", (string filename, string? filepath, DownloadQueue queue) =>
{
    if (!string.IsNullOrEmpty(filepath) && File.Exists(filepath))
    {
        var sz = new FileInfo(filepath).Length;
        return new CheckExistsResponse(true, sz, filepath);
    }

    var filePath = Path.Combine(GetDownloadBasePath(queue), "completed", filename);
    var exists = File.Exists(filePath);
    long? size = exists ? new FileInfo(filePath).Length : null;
    return new CheckExistsResponse(exists, size, filePath);
});

// --- Partial file check ---

app.MapGet("/api/partials", (DownloadQueue queue) =>
{
    var dlPath = Path.Combine(GetDownloadBasePath(queue), "downloading");

    if (!Directory.Exists(dlPath))
        return new PartialsResponse(null, []);

    var files = Directory.GetFiles(dlPath)
        .Select(f => new FileInfo(f))
        .Where(f => f.Length > 0)
        .Select(f => new PartialFile(f.Name, f.Length, Math.Round(f.Length / 1048576.0, 2)))
        .ToList();

    return new PartialsResponse(dlPath, files);
});

// --- Metadata API ---

app.MapGet("/api/meta", async (string url, IHttpClientFactory httpFactory, QueueRepository repo) =>
{
    var cached = repo.GetMeta(url);
    if (cached != null)
        return cached;

    try
    {
        var http = httpFactory.CreateClient("vimms");
        var html = await http.GetStringAsync(url);

        var titleMatch = Regex.Match(html, @"<title>(?:The Vault:\s*)?(.+?)\s*</title>", RegexOptions.IgnoreCase);
        var fullTitle = WebUtility.HtmlDecode(
            titleMatch.Success ? titleMatch.Groups[1].Value.Trim() : "Unknown");

        var platformMatch = Regex.Match(html, @"class=""sectionTitle""[^>]*>\s*([^<]+?)\s*</div>", RegexOptions.IgnoreCase);
        if (!platformMatch.Success)
            platformMatch = Regex.Match(html, @"<h2[^>]*>\s*([^<]+?)\s*</h2>", RegexOptions.IgnoreCase);
        var platform = WebUtility.HtmlDecode(
            platformMatch.Success ? platformMatch.Groups[1].Value.Trim() : "");

        var title = Regex.Replace(fullTitle, @"\s*\([^)]*\)\s*$", "").Trim();
        if (string.IsNullOrEmpty(title)) title = fullTitle;

        var sizeMatch = Regex.Match(html, @"([\d,.]+)\s*(GB|MB|KB)", RegexOptions.IgnoreCase);
        var size = sizeMatch.Success ? $"{sizeMatch.Groups[1].Value} {sizeMatch.Groups[2].Value}" : "";

        // Parse format options from dl_format select (PS3 games have JB Folder / .dec.iso)
        string? formats = null;
        var formatMatches = Regex.Matches(html, @"<option\s+value=""(\d+)""\s+title=""([^""]+)"">([^<]+)</option>",
            RegexOptions.IgnoreCase);
        if (formatMatches.Count > 1)
        {
            var zippedTextMatch = Regex.Match(html, @"""ZippedText""\s*:\s*""([^""]+)""");
            var altZippedTextMatch = Regex.Match(html, @"""AltZippedText""\s*:\s*""([^""]+)""");

            var fmtList = formatMatches.Select(m => new FormatOption(
                int.Parse(m.Groups[1].Value),
                WebUtility.HtmlDecode(m.Groups[3].Value.Trim()),
                WebUtility.HtmlDecode(m.Groups[2].Value.Trim()),
                int.Parse(m.Groups[1].Value) == 0 && zippedTextMatch.Success ? zippedTextMatch.Groups[1].Value
                    : int.Parse(m.Groups[1].Value) == 1 && altZippedTextMatch.Success ? altZippedTextMatch.Groups[1].Value
                    : ""
            )).ToList();
            formats = JsonSerializer.Serialize(fmtList, AppJsonContext.Default.ListFormatOption);
        }

        repo.SaveMeta(url, title, platform, size, formats);
        return new MetaResponse(title, platform, size, formats);
    }
    catch
    {
        return new MetaResponse(url.Split('/').Last(), "", "", null);
    }
});

// --- Config APIs ---

app.MapGet("/api/config", (DownloadQueue queue) =>
{
    var isWindows = OperatingSystem.IsWindows();
    var isLinux = OperatingSystem.IsLinux();
    var isMac = OperatingSystem.IsMacOS();
    var platformName = isWindows ? "windows" : isLinux ? "linux" : isMac ? "macos" : "unknown";
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    var defaultPath = Path.Combine(home, "Downloads");

    var configuredPath = app.Configuration.GetValue<string>("DownloadPath");
    var activePath = string.IsNullOrWhiteSpace(configuredPath) ? defaultPath : configuredPath;

    return new ConfigResponse(platformName, System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        Environment.MachineName, Environment.UserName, defaultPath, activePath,
        queue.IsRunning, queue.CurrentFile, queue.CurrentProgress);
});

app.MapPost("/api/config/check-path", (SetPathRequest req) =>
{
    var path = ExpandPath(req.Path);
    if (string.IsNullOrEmpty(path))
        return new CheckPathResponse(path, false, false, null, "empty path");

    var exists = Directory.Exists(path);
    var writable = false;
    string? error = null;
    long? freeSpace = null;

    if (exists)
    {
        try
        {
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

    return new CheckPathResponse(path, exists, writable, freeSpace, error);
});

// --- Status API (for reconnecting clients) ---

app.MapGet("/api/status", (DownloadQueue queue, QueueRepository repo) =>
{
    var dlPath = Path.Combine(GetDownloadBasePath(queue), "downloading");

    List<PartialFile>? partials = null;
    if (repo.HasQueuedUrls() && !queue.IsRunning && Directory.Exists(dlPath))
    {
        partials = [];
        foreach (var file in Directory.GetFiles(dlPath))
        {
            var fi = new FileInfo(file);
            if (fi.Length > 0)
                partials.Add(new PartialFile(fi.Name, fi.Length, fi.Length / 1048576.0));
        }
    }

    return new StatusResponse(queue.IsRunning, queue.IsPaused, queue.CurrentFile, queue.CurrentUrl,
        queue.CurrentProgress, queue.TotalBytes, queue.DownloadedBytes, queue.GetRecentLogs(), partials);
});

// --- PS3 ISO Conversion ---

app.MapPost("/api/convert-ps3", (DownloadQueue queue, Ps3ConversionPipeline pipeline) =>
{
    var basePath = GetDownloadBasePath(queue);
    var completedDir = Path.Combine(basePath, "completed");
    var tempBaseDir = Path.Combine(basePath, "ps3_temp");

    if (!Directory.Exists(completedDir))
        return new ConvertPs3Response(0, 0, []);

    // Scan filesystem directly — picks up all archives regardless of DB state.
    // Non-PS3 archives are skipped gracefully by FindJbFolder in the pipeline.
    var archiveExts = new[] { ".zip", ".7z", ".rar" };
    int queued = 0, skipped = 0;
    var files = new List<string>();

    foreach (var filepath in Directory.GetFiles(completedDir))
    {
        var ext = Path.GetExtension(filepath);
        if (!archiveExts.Any(e => ext.Equals(e, StringComparison.OrdinalIgnoreCase)))
            continue;

        if (pipeline.Enqueue(filepath, completedDir, tempBaseDir))
        {
            queued++;
            files.Add(Path.GetFileName(filepath));
        }
        else skipped++;
    }

    return new ConvertPs3Response(queued, skipped, files);
});

app.MapPost("/api/convert-ps3/single", (ConvertSingleRequest req, DownloadQueue queue, Ps3ConversionPipeline pipeline) =>
{
    var basePath = GetDownloadBasePath(queue);
    var completedDir = Path.Combine(basePath, "completed");
    var tempBaseDir = Path.Combine(basePath, "ps3_temp");
    var filepath = Path.Combine(completedDir, req.Filename);

    if (!File.Exists(filepath))
        return Results.NotFound();

    var enqueued = pipeline.Enqueue(filepath, completedDir, tempBaseDir, force: true);
    return Results.Ok(new ConvertSingleResponse(enqueued, req.Filename));
});

app.MapPost("/api/convert-ps3/mark-done", (ConvertSingleRequest req, Ps3ConversionPipeline pipeline) =>
{
    pipeline.MarkConverted(req.Filename);
    return Results.Ok();
});

app.MapPost("/api/convert-ps3/abort", (ConvertSingleRequest req, Ps3ConversionPipeline pipeline) =>
{
    var aborted = pipeline.Abort(req.Filename);
    return Results.Ok(new AbortResponse(aborted));
});

app.MapGet("/api/convert-ps3/status", (Ps3ConversionPipeline pipeline) =>
    pipeline.GetStatuses());

app.Run();

// --- Helpers ---

string GetDownloadBasePath(DownloadQueue queue)
{
    var active = queue.ActiveDownloadPath;
    if (!string.IsNullOrEmpty(active))
    {
        var trimmed = active.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed.EndsWith("downloading") ? Path.GetDirectoryName(active)! : active;
    }

    return app.Configuration.GetValue<string>("DownloadPath")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
}

static string? ExpandPath(string? p)
{
    p = p?.Trim();
    if (string.IsNullOrEmpty(p)) return p;
    if (p.StartsWith("~/"))
        p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), p[2..]);
    return p;
}

// --- Records ---

record SetPathRequest(string Path);
record AddRequest(List<string> Urls, int? Format = null);
record MoveRequest(int Id, string Direction);
record SetFormatRequest(int Id, int Format);

record VersionResponse(string Current, string? Latest, bool HasUpdate, string? Url, string? Changelog);
record DataResponse(List<QueuedItem> Queued, List<CompletedItem> Completed);
record QueueListResponse(List<QueueIdRow> Queued);
record QueueIdRow(int Id, string Url, int Format);
record QueuedItem(int Id, string Url, int Format, string? Title, string? Platform, string? Size, string? Formats);
record CompletedItem(int Id, string Url, string Filename, string? Filepath);
record MetaResponse(string Title, string Platform, string Size, string? Formats);
record FormatOption(int Value, string Label, string Title, string Size);
record CheckExistsResponse(bool Exists, long? Size, string Path);
record PartialsResponse(string? Path, List<PartialFile> Files);
record PartialFile(string Name, long Bytes, double Mb);
record ConfigResponse(string Platform, string OsDescription, string Hostname, string User,
    string DefaultPath, string ActivePath, bool IsRunning, string? CurrentFile, string? Progress);
record CheckPathResponse(string? Path, bool Exists, bool Writable, long? FreeSpace, string? Error);
record StatusResponse(bool IsRunning, bool IsPaused, string? CurrentFile, string? CurrentUrl,
    string? Progress, long TotalBytes, long DownloadedBytes, List<LogEntry> RecentLogs, List<PartialFile>? Partials);
record LogEntry(string Time, string Type, string Message);
record CompletedEvent(string Url, string Filename, string Filepath);
record ConvertStatusUpdate(string ZipName, string Phase, string Message);
record ConvertPs3Response(int Queued, int Skipped, List<string> Files);
record ConvertSingleRequest(string Filename);
record ConvertSingleResponse(bool Enqueued, string Filename);
record AbortResponse(bool Aborted);

// Shared lock for queue mutations (move, delete, complete)
static class QueueLock
{
    public static readonly object Sync = new();
}

// --- JSON Source Generator (AOT-compatible) ---

[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(VersionResponse))]
[JsonSerializable(typeof(DataResponse))]
[JsonSerializable(typeof(QueueListResponse))]
[JsonSerializable(typeof(QueueIdRow))]
[JsonSerializable(typeof(QueuedItem))]
[JsonSerializable(typeof(CompletedItem))]
[JsonSerializable(typeof(MetaResponse))]
[JsonSerializable(typeof(FormatOption))]
[JsonSerializable(typeof(List<FormatOption>))]
[JsonSerializable(typeof(CheckExistsResponse))]
[JsonSerializable(typeof(PartialsResponse))]
[JsonSerializable(typeof(PartialFile))]
[JsonSerializable(typeof(ConfigResponse))]
[JsonSerializable(typeof(CheckPathResponse))]
[JsonSerializable(typeof(StatusResponse))]
[JsonSerializable(typeof(LogEntry))]
[JsonSerializable(typeof(CompletedEvent))]
[JsonSerializable(typeof(AddRequest))]
[JsonSerializable(typeof(MoveRequest))]
[JsonSerializable(typeof(SetFormatRequest))]
[JsonSerializable(typeof(SetPathRequest))]
[JsonSerializable(typeof(List<QueuedItem>))]
[JsonSerializable(typeof(List<CompletedItem>))]
[JsonSerializable(typeof(List<QueueIdRow>))]
[JsonSerializable(typeof(List<PartialFile>))]
[JsonSerializable(typeof(List<LogEntry>))]
[JsonSerializable(typeof(ConvertStatusUpdate))]
[JsonSerializable(typeof(List<ConvertStatusUpdate>))]
[JsonSerializable(typeof(ConvertPs3Response))]
[JsonSerializable(typeof(ConvertSingleRequest))]
[JsonSerializable(typeof(ConvertSingleResponse))]
[JsonSerializable(typeof(AbortResponse))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class AppJsonContext : JsonSerializerContext;

// --- Repository (raw ADO.NET, AOT-safe) ---

class QueueRepository
{
    private string _connStr = "Data Source=queue.db";

    public SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connStr);
        conn.Open();
        return conn;
    }

    public void Init(string? configConnStr)
    {
        if (!string.IsNullOrEmpty(configConnStr))
        {
            _connStr = configConnStr;
            var dbPath = configConnStr.Replace("Data Source=", "").Trim();
            var dbDir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dbDir)) Directory.CreateDirectory(dbDir);
        }

        using var db = Open();
        Exec(db, "PRAGMA journal_mode=WAL");
        Exec(db, """
            CREATE TABLE IF NOT EXISTS queued_urls (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                url TEXT NOT NULL,
                format INTEGER NOT NULL DEFAULT 0
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
                size TEXT NOT NULL,
                formats TEXT
            );
        """);
        try { Exec(db, "ALTER TABLE queued_urls ADD COLUMN format INTEGER NOT NULL DEFAULT 0"); } catch { }
        try { Exec(db, "ALTER TABLE url_meta ADD COLUMN formats TEXT"); } catch { }
    }

    public bool HasQueuedUrls()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM queued_urls";
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public List<QueuedItem> GetQueuedItems()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT q.id, q.url, q.format, m.title, m.platform, m.size, m.formats
            FROM queued_urls q LEFT JOIN url_meta m ON q.url = m.url
            ORDER BY q.id
        """;
        var items = new List<QueuedItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            items.Add(new QueuedItem(r.GetInt32(0), r.GetString(1), r.GetInt32(2),
                r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6)));
        return items;
    }

    public List<CompletedItem> GetCompletedItems()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT id, url, filename, filepath FROM completed_urls";
        var items = new List<CompletedItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            items.Add(new CompletedItem(r.GetInt32(0), r.GetString(1), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3)));
        return items;
    }

    public List<QueueIdRow> GetQueueIds()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT id, url, format FROM queued_urls";
        var items = new List<QueueIdRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            items.Add(new QueueIdRow(r.GetInt32(0), r.GetString(1), r.GetInt32(2)));
        return items;
    }

    public void AddToQueue(string url, int format)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO queued_urls (url, format) VALUES ($url, $format)";
        cmd.Parameters.AddWithValue("$url", url);
        cmd.Parameters.AddWithValue("$format", format);
        cmd.ExecuteNonQuery();
    }

    public void DeleteFromQueue(int id)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM queued_urls WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public bool MoveInQueue(int id, string direction)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        try
        {
            var ids = new List<int>();
            using (var cmd = db.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT id FROM queued_urls ORDER BY id";
                using var r = cmd.ExecuteReader();
                while (r.Read()) ids.Add(r.GetInt32(0));
            }

            var idx = ids.IndexOf(id);
            if (idx < 0) { tx.Rollback(); return false; }
            var targetIdx = direction == "up" ? idx - 1 : idx + 1;
            if (targetIdx < 0 || targetIdx >= ids.Count) { tx.Rollback(); return true; }

            var otherId = ids[targetIdx];
            ExecTx(db, tx, "UPDATE queued_urls SET id = -999 WHERE id = $id", ("$id", id));
            ExecTx(db, tx, "UPDATE queued_urls SET id = $newId WHERE id = $otherId", ("$newId", id), ("$otherId", otherId));
            ExecTx(db, tx, "UPDATE queued_urls SET id = $newId WHERE id = -999", ("$newId", otherId));
            tx.Commit();
            return true;
        }
        catch { tx.Rollback(); return false; }
    }

    public void ClearQueue()
    {
        using var db = Open();
        Exec(db, "DELETE FROM queued_urls");
    }

    public void SetFormat(int id, int format)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE queued_urls SET format = $format WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$format", format);
        cmd.ExecuteNonQuery();
    }

    public MetaResponse? GetMeta(string url)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT title, platform, size, formats FROM url_meta WHERE url = $url";
        cmd.Parameters.AddWithValue("$url", url);
        using var r = cmd.ExecuteReader();
        if (!r.Read() || r.IsDBNull(0)) return null;

        var title = WebUtility.HtmlDecode(r.GetString(0));
        var platform = WebUtility.HtmlDecode(r.GetString(1));
        var size = r.GetString(2);
        var formats = r.IsDBNull(3) ? null : r.GetString(3);

        // Update decoded values if they changed
        if (title != r.GetString(0) || platform != r.GetString(1))
        {
            using var upd = db.CreateCommand();
            upd.CommandText = "UPDATE url_meta SET title=$t, platform=$p WHERE url=$url";
            upd.Parameters.AddWithValue("$t", title);
            upd.Parameters.AddWithValue("$p", platform);
            upd.Parameters.AddWithValue("$url", url);
            upd.ExecuteNonQuery();
        }

        return new MetaResponse(title, platform, size, formats);
    }

    public void SaveMeta(string url, string title, string platform, string size, string? formats)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO url_meta (url, title, platform, size, formats)
            VALUES ($url, $title, $platform, $size, $formats)
        """;
        cmd.Parameters.AddWithValue("$url", url);
        cmd.Parameters.AddWithValue("$title", title);
        cmd.Parameters.AddWithValue("$platform", platform);
        cmd.Parameters.AddWithValue("$size", size);
        cmd.Parameters.AddWithValue("$formats", (object?)formats ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    // --- Used by DownloadQueue ---

    public (int Id, string Url, int Format)? GetNextQueueItem()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT id, url, format FROM queued_urls ORDER BY id LIMIT 1";
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return (r.GetInt32(0), r.GetString(1), r.GetInt32(2));
    }

    public void CompleteItem(int id, string url, string filename, string filepath)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        try
        {
            ExecTx(db, tx, "DELETE FROM queued_urls WHERE id = $id", ("$id", id));
            using var ins = db.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT INTO completed_urls (url, filename, filepath) VALUES ($url, $filename, $filepath)";
            ins.Parameters.AddWithValue("$url", url);
            ins.Parameters.AddWithValue("$filename", filename);
            ins.Parameters.AddWithValue("$filepath", filepath);
            ins.ExecuteNonQuery();
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public void MoveToFront(int queueId)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT MIN(id) FROM queued_urls";
        var minId = cmd.ExecuteScalar();
        if (minId is long min && queueId != min)
        {
            using var upd = db.CreateCommand();
            upd.CommandText = "UPDATE queued_urls SET id = $newId WHERE id = $queueId";
            upd.Parameters.AddWithValue("$newId", min - 1);
            upd.Parameters.AddWithValue("$queueId", queueId);
            upd.ExecuteNonQuery();
        }
    }

    public void DeleteCompleted(int id)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM completed_urls WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public List<string> GetCompletedPs3FilePaths()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT c.filepath FROM completed_urls c
            JOIN url_meta m ON c.url = m.url
            WHERE LOWER(m.platform) = 'playstation 3'
            AND c.filepath IS NOT NULL
        """;
        var paths = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            if (!r.IsDBNull(0)) paths.Add(r.GetString(0));
        return paths;
    }

    // --- Internal helpers ---

    private static void Exec(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void ExecTx(SqliteConnection db, SqliteTransaction tx, string sql,
        params (string Name, object Value)[] parameters)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }
}

// --- SignalR Hub ---

class DownloadHub : Hub
{
    private readonly DownloadQueue _queue;
    private readonly QueueRepository _repo;
    public DownloadHub(DownloadQueue queue, QueueRepository repo) { _queue = queue; _repo = repo; }

    public async Task StartDownload(string? downloadPath)
    {
        if (_queue.IsRunning)
        {
            _queue.Pause();
            var timeout = DateTime.UtcNow.AddSeconds(10);
            while (_queue.IsRunning && DateTime.UtcNow < timeout)
                await Task.Delay(200);
        }
        _queue.Start(downloadPath);
    }

    public async Task StartSpecific(string? downloadPath, int queueId)
    {
        _repo.MoveToFront(queueId);

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

// --- Download Queue Service ---

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
                catch (Exception ex)
                {
                    _log.LogError(ex, "Error downloading {Url}", url);
                    await Emit("Error", $"Failed: {url} - {ex.Message}");
                    _repo.DeleteFromQueue(id);
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

// --- PS3 ISO Conversion Pipeline ---
// Two-phase pipeline with configurable parallelism:
//   N extractors read from _extractQueue concurrently
//   N converters read from _convertQueue concurrently
// With MaxParallelism=3 and 30 queued zips:
//   3 zips extract simultaneously, as each finishes it enters conversion,
//   3 conversions run simultaneously, all phases overlap.

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

    /// <summary>
    /// Clean up orphaned temp dirs and temp ISOs from previous crashes.
    /// Must be called before any Enqueue (startup only).
    /// </summary>
    public void CleanupOrphans(string downloadBasePath)
    {
        var tempDir = Path.Combine(downloadBasePath, "ps3_temp");
        if (Directory.Exists(tempDir))
        {
            foreach (var dir in Directory.GetDirectories(tempDir))
            {
                _log.LogInformation("Cleaning orphaned PS3 temp dir: {Path}", dir);
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        var completedDir = Path.Combine(downloadBasePath, "completed");
        if (Directory.Exists(completedDir))
        {
            foreach (var f in Directory.GetFiles(completedDir, "temp_*.iso"))
            {
                _log.LogInformation("Cleaning orphaned temp ISO: {Path}", f);
                try { File.Delete(f); } catch { }
            }
        }

        // Load previously converted list and pre-populate statuses
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

    /// <summary>
    /// Mark an archive as already converted (without actually running conversion).
    /// </summary>
    public void MarkConverted(string filename)
    {
        _statuses[filename] = new ConvertStatusUpdate(filename, "done", "Marked as converted");
        AddToConvertedList(filename);
    }

    /// <summary>
    /// Queue a zip for extraction → ISO conversion.
    /// Returns false if the zip is already queued or being processed.
    /// </summary>
    /// <summary>
    /// Queue a zip for extraction → ISO conversion.
    /// When force=false (Convert All), skips already-converted items.
    /// When force=true (per-item button), always enqueues.
    /// </summary>
    public bool Enqueue(string zipPath, string completedDir, string tempBaseDir, bool force = false)
    {
        var key = Path.GetFileName(zipPath);

        // "Convert All" skips already-converted archives
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
        // If queued but not yet started, just mark as aborted
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

    private async Task EmitStatus(string zipName, string phase, string message)
    {
        var update = new ConvertStatusUpdate(zipName, phase, message);
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

            // Get per-job cancellation token (or skip if already aborted)
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

                // Check disk space: extraction + ISO needs ~3x the zip size
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
                catch { /* DriveInfo may fail on some platforms, continue anyway */ }

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
                    await EmitStatus(job.ZipName, "done", $"ISO ready: {isoName}");
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
                try { if (Directory.Exists(job.TempDir)) Directory.Delete(job.TempDir, true); } catch { }
            }
        }
    }
}
