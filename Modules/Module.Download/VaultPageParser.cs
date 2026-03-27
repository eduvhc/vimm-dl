using System.Net;
using System.Text.RegularExpressions;

namespace Module.Download;

/// <summary>Extracts download metadata from a Vimm's Lair vault page.</summary>
public static partial class VaultPageParser
{
    [GeneratedRegex(@"name=""mediaId""\s+value=""(\d+)""", RegexOptions.IgnoreCase)]
    private static partial Regex MediaIdRegex1();

    [GeneratedRegex(@"value=""(\d+)""\s+name=""mediaId""", RegexOptions.IgnoreCase)]
    private static partial Regex MediaIdRegex2();

    [GeneratedRegex(@"<title>(?:The Vault:\s*)?(.+?)\s*</title>", RegexOptions.IgnoreCase)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"id=""dl_form""[^>]*action=""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex FormAction1();

    [GeneratedRegex(@"action=""([^""]+)""[^>]*id=""dl_form""", RegexOptions.IgnoreCase)]
    private static partial Regex FormAction2();

    [GeneratedRegex(@"\.action\s*=\s*['""]([^'""]+)['""]", RegexOptions.IgnoreCase)]
    private static partial Regex JsActionRegex();

    [GeneratedRegex(@"(https?://dl\d*\.vimm\.net/?)", RegexOptions.IgnoreCase)]
    private static partial Regex DlServerRegex();

    [GeneratedRegex(@"(//dl\d*\.vimm\.net/?)", RegexOptions.IgnoreCase)]
    private static partial Regex DlServerProtoRelRegex();

    public record ParseResult(string MediaId, string Title, string DownloadUrl);

    public static ParseResult? Parse(string html, string vaultUrl, int format)
    {
        var mediaId = ExtractMediaId(html);
        if (mediaId == null) return null;

        var titleMatch = TitleRegex().Match(html);
        var title = titleMatch.Success
            ? WebUtility.HtmlDecode(titleMatch.Groups[1].Value.Trim())
            : "download";

        var dlServer = ResolveDlServer(html, vaultUrl);
        var downloadUrl = format > 0
            ? $"{dlServer}?mediaId={mediaId}&alt={format}"
            : $"{dlServer}?mediaId={mediaId}";

        return new ParseResult(mediaId, title, downloadUrl);
    }

    internal static string? ExtractMediaId(string html)
    {
        var m = MediaIdRegex1().Match(html);
        if (!m.Success) m = MediaIdRegex2().Match(html);
        return m.Success ? m.Groups[1].Value : null;
    }

    internal static string ResolveDlServer(string html, string vaultUrl)
    {
        string? dlServer = null;

        var actionMatch = FormAction1().Match(html);
        if (!actionMatch.Success) actionMatch = FormAction2().Match(html);
        if (actionMatch.Success) dlServer = actionMatch.Groups[1].Value;

        if (dlServer == null)
        {
            var jsAction = JsActionRegex().Match(html);
            if (jsAction.Success) dlServer = jsAction.Groups[1].Value;
        }

        if (dlServer == null)
        {
            var dlMatch = DlServerRegex().Match(html);
            if (dlMatch.Success) dlServer = dlMatch.Groups[1].Value;
        }

        if (dlServer == null)
        {
            var prMatch = DlServerProtoRelRegex().Match(html);
            if (prMatch.Success) dlServer = "https:" + prMatch.Groups[1].Value;
        }

        dlServer ??= "https://dl3.vimm.net/";

        var pageUri = new Uri(vaultUrl);
        Uri dlBaseUri;
        if (dlServer.StartsWith("//"))
            dlBaseUri = new Uri($"https:{dlServer}");
        else if (Uri.TryCreate(dlServer, UriKind.Absolute, out var abs) && (abs.Scheme == "http" || abs.Scheme == "https"))
            dlBaseUri = abs;
        else
            dlBaseUri = new Uri(pageUri, dlServer);

        if (dlBaseUri.Scheme != "https")
            dlBaseUri = new Uri($"https://{dlBaseUri.Host}{dlBaseUri.AbsolutePath}");

        return dlBaseUri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/";
    }
}
