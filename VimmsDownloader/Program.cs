using System.Net;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR()
    .AddJsonProtocol(o =>
        o.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));
builder.Services.AddSingleton<QueueRepository>();
builder.Services.AddSingleton<Module.Ps3Iso.Bridge.IPs3IsoBridge, SignalRPs3IsoBridge>();
builder.Services.AddSingleton<Module.Ps3Iso.Ps3ConversionPipeline>();
builder.Services.AddSingleton<DownloadQueue>();
builder.Services.AddSingleton<Module.Sync.Bridge.ISyncBridge, SignalRSyncBridge>();
builder.Services.AddSingleton<Module.Sync.SyncService>();
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
    var ps3Pipeline = app.Services.GetRequiredService<Module.Ps3Iso.Ps3ConversionPipeline>();
    ps3Pipeline.Configure(app.Configuration.GetValue("Ps3ConvertParallelism", 3));
    ps3Pipeline.CleanupOrphans(dlBase);
    app.Services.GetRequiredService<Module.Sync.SyncService>().Configure(
        dlBase, app.Configuration.GetValue<string>("SyncPath") ?? "");
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

// Map API endpoints
app.MapFileEndpoints();
app.MapDownloadEndpoints();
app.MapMetadataEndpoints();
app.MapConfigEndpoints();
app.MapPs3Endpoints();
app.MapSyncEndpoints();

app.Run();
