using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

static class MetadataEndpoints
{
    private static readonly string CurrentVersion =
        typeof(MetadataEndpoints).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static void MapMetadataEndpoints(this IEndpointRouteBuilder app)
    {
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

            var hasUpdate = latest != null && latest != CurrentVersion &&
                Version.TryParse(latest, out var lv) && Version.TryParse(CurrentVersion, out var cv) && lv > cv;

            return new VersionResponse(CurrentVersion, latest, hasUpdate, url, changelog);
        });

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
    }
}
