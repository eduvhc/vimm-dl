using System.Net;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR()
    .AddJsonProtocol(o =>
        o.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default));
builder.Services.AddSingleton<QueueRepository>();
builder.Services.AddSingleton<Module.Ps3Pipeline.Bridge.IPs3PipelineBridge, SignalRPs3PipelineBridge>();
builder.Services.AddSingleton<Module.Ps3Pipeline.Ps3ConversionPipeline>();
builder.Services.AddSingleton<Module.Download.Bridge.IDownloadBridge, SignalRDownloadBridge>();
builder.Services.AddSingleton<Module.Download.DownloadService>();
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
    var dlBase = repo.GetDownloadPath();
    var ps3Pipeline = app.Services.GetRequiredService<Module.Ps3Pipeline.Ps3ConversionPipeline>();
    var parallelism = int.TryParse(repo.GetSetting(SettingsKeys.Ps3Parallelism), out var p) ? p : 3;
    ps3Pipeline.Configure(parallelism);
    ps3Pipeline.CleanupOrphans(dlBase);
    app.Services.GetRequiredService<Module.Sync.SyncService>().Configure(dlBase, repo.GetSyncPath());
}

// Auto-resume: if there are queued URLs, start downloading on app launch
if (repo.HasQueuedUrls())
{
    _ = Task.Run(async () =>
    {
        await Task.Delay(1500);
        var queue = app.Services.GetRequiredService<DownloadQueue>();
        if (!queue.IsRunning)
            queue.Start(repo.GetDownloadPath());
    });
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHub<DownloadHub>("/hub");

// Map API endpoints
app.MapFileEndpoints();
app.MapDownloadEndpoints();
app.MapMetadataEndpoints();
app.MapPs3Endpoints();
app.MapSyncEndpoints();
app.MapSettingsEndpoints();

// Auto-resume: if there are queued items, start downloading on startup
{
    var logger = app.Services.GetRequiredService<ILogger<DownloadQueue>>();
    var queueRepo = app.Services.GetRequiredService<QueueRepository>();
    var dlQueue = app.Services.GetRequiredService<DownloadQueue>();
    var hasQueued = queueRepo.HasQueuedUrls();
    logger.LogInformation("Auto-resume check: hasQueued={HasQueued}, isRunning={IsRunning}", hasQueued, dlQueue.IsRunning);
    if (hasQueued && !dlQueue.IsRunning)
    {
        logger.LogInformation("Auto-resuming download queue");
        dlQueue.Start(null);
    }
}

app.Run();
