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
            var result = await MetadataFetcher.FetchAndCache(url, httpFactory, repo);
            return result ?? new MetaResponse(url.Split('/').Last(), "", "", null, null);
        });
    }
}
