// Mock Vimm's Lair server for testing VimmsDownloader
// Run this on port 5111, then queue URLs like http://localhost:5111/vault/1234

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var games = new Dictionary<int, (string Title, string System, int MediaId, int SizeMB)>
{
    [1001] = ("Super Mario Bros", "NES", 9001, 2),
    [1002] = ("The Legend of Zelda", "NES", 9002, 3),
    [1003] = ("Sonic the Hedgehog", "Genesis", 9003, 1),
    [1004] = ("Castlevania", "NES", 9004, 2),
    [1005] = ("Mega Man 2", "NES", 9005, 1),
    [1006] = ("Street Fighter II", "SNES", 9006, 4),
    [1007] = ("Final Fantasy VII", "PS1", 9007, 50),
    [1008] = ("Pokemon Red", "GB", 9008, 1),
    [1009] = ("Metroid", "NES", 9009, 1),
    [1010] = ("Chrono Trigger", "SNES", 9010, 4),
};

// Vault page — returns HTML matching the real site's structure
app.MapGet("/vault/{id:int}", (int id) =>
{
    if (!games.TryGetValue(id, out var game))
        return Results.NotFound("Game not found");

    var html = $"""
    <!DOCTYPE html>
    <html>
    <head><title>The Vault: {game.Title} ({game.System})</title></head>
    <body>
        <h2>{game.System}</h2>
        <div id="data-good-title">{game.Title}.7z</div>

        <form id="dl_form" action="http://localhost:5111/download/" method="post"
              onsubmit="return submitDL(this, 'tooltip1')">
            <input type="hidden" name="mediaId" value="{game.MediaId}">
            <input type="hidden" name="alt" value="0">
            <button type="submit" style="width:100%">Download</button>
        </form>

        <script>
            function submitDL(theForm, tooltipId) {"{"} theForm.method='GET'; return true; {"}"}
        </script>

        <p>File size: ~{game.SizeMB} MB</p>
    </body>
    </html>
    """;

    return Results.Content(html, "text/html");
});

// Download endpoint — streams a fake file with realistic behavior
app.MapGet("/download/", async (int mediaId, int alt, HttpContext ctx) =>
{
    var game = games.Values.FirstOrDefault(g => g.MediaId == mediaId);
    if (game == default)
    {
        ctx.Response.StatusCode = 404;
        await ctx.Response.WriteAsync("Not found");
        return;
    }

    var filename = $"{game.Title} ({game.System}).7z";
    var totalBytes = game.SizeMB * 1024 * 1024;

    ctx.Response.StatusCode = 200;
    ctx.Response.ContentType = "application/octet-stream";
    ctx.Response.ContentLength = totalBytes;
    ctx.Response.Headers["Content-Disposition"] = $"attachment; filename=\"{filename}\"";

    // Stream fake data in chunks, throttled to simulate a real download
    var buffer = new byte[65536];
    Random.Shared.NextBytes(buffer); // fill with random junk
    var sent = 0;

    while (sent < totalBytes)
    {
        var chunk = Math.Min(buffer.Length, totalBytes - sent);
        await ctx.Response.Body.WriteAsync(buffer.AsMemory(0, chunk));
        sent += chunk;

        // Throttle: ~2 MB/s
        await Task.Delay(30);
    }
});

// Index page — list all available mock games
app.MapGet("/", () =>
{
    var rows = string.Join("\n", games.Select(g =>
        $"  <tr><td><a href='/vault/{g.Key}'>/vault/{g.Key}</a></td><td>{g.Value.Title}</td><td>{g.Value.System}</td><td>{g.Value.SizeMB} MB</td></tr>"));

    var html = $"""
    <!DOCTYPE html>
    <html>
    <head>
        <title>Mock Vimm's Lair</title>
        <style>
            body {"{"} font-family: monospace; background: #111; color: #0f0; padding: 20px; {"}"}
            table {"{"} border-collapse: collapse; margin-top: 10px; {"}"}
            td, th {"{"} border: 1px solid #333; padding: 6px 12px; text-align: left; {"}"}
            a {"{"} color: #0ff; {"}"}
            th {"{"} background: #222; color: #ff0; {"}"}
            h1 {"{"} color: #0f0; {"}"}
            pre {"{"} color: #888; font-size: 12px; margin-top: 20px; {"}"}
        </style>
    </head>
    <body>
        <h1>[MOCK] Vimm's Lair Test Server</h1>
        <p>Port 5111 — use these URLs in VimmsDownloader to test</p>
        <table>
            <tr><th>URL</th><th>Title</th><th>System</th><th>Size</th></tr>
            {rows}
        </table>
        <pre>
    Tip: paste these into the downloader:
    http://localhost:5111/vault/1001
    http://localhost:5111/vault/1002
    http://localhost:5111/vault/1003
    http://localhost:5111/vault/1004
    http://localhost:5111/vault/1005
        </pre>
    </body>
    </html>
    """;

    return Results.Content(html, "text/html");
});

app.Urls.Add("http://localhost:5111");
app.Run();
