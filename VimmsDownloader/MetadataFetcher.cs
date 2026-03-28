using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR;

static class MetadataFetcher
{
    public static async Task<MetaResponse?> FetchAndCache(string url, IHttpClientFactory httpFactory, QueueRepository repo)
    {
        var cached = await repo.GetMetaAsync(url);
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

            var serialMatch = Regex.Match(html, @"""Serial""\s*:\s*""([A-Z]{4}-\d{5})""");
            if (!serialMatch.Success)
                serialMatch = Regex.Match(html, @"Serial\s*#?\s*\n?\s*([A-Z]{4}-\d{5})");
            var serial = serialMatch.Success ? serialMatch.Groups[1].Value : null;

            await repo.SaveMetaAsync(url, title, platform, size, formats, serial);
            return new MetaResponse(title, platform, size, formats, serial);
        }
        catch
        {
            return null;
        }
    }

    public static void FetchInBackground(List<string> urls, IServiceProvider services, ILogger logger)
    {
        _ = Task.Run(async () =>
        {
            var httpFactory = services.GetRequiredService<IHttpClientFactory>();
            var repo = services.GetRequiredService<QueueRepository>();
            var hub = services.GetRequiredService<IHubContext<DownloadHub>>();

            foreach (var url in urls)
            {
                try
                {
                    var result = await FetchAndCache(url, httpFactory, repo);
                    if (result != null)
                    {
                        // Notify frontend to refresh queue data (title, formats now available)
                        await hub.Clients.All.SendAsync("MetaReady", url);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to fetch metadata for {Url}", url);
                }
                await Task.Delay(300);
            }

            logger.LogInformation("Background metadata fetch complete for {Count} URLs", urls.Count);
        });
    }
}
