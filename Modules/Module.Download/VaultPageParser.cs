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

    [GeneratedRegex(@"<option\s+value=""(\d+)""\s+title=""[^""]*"">[^<]+</option>", RegexOptions.IgnoreCase)]
    private static partial Regex FormatOptionRegex();

    public record ParseResult(string MediaId, string Title, string DownloadUrl, int ResolvedFormat, string? FormatNote);

    /// <summary>
    /// Parse the vault page HTML. Resolves format with fallback:
    /// preferred format → fallback to 0 → error if neither available.
    /// FormatNote describes what happened (null = used as requested).
    /// </summary>
    public static ParseResult? Parse(string html, string vaultUrl, int preferredFormat)
    {
        var mediaId = ExtractMediaId(html);
        if (mediaId == null) return null;

        var titleMatch = TitleRegex().Match(html);
        var title = titleMatch.Success
            ? WebUtility.HtmlDecode(titleMatch.Groups[1].Value.Trim())
            : "download";

        var availableFormats = ExtractAvailableFormats(html);
        var (resolvedFormat, formatNote) = ResolveFormat(preferredFormat, availableFormats);

        var dlServer = ResolveDlServer(html, vaultUrl);
        var downloadUrl = resolvedFormat > 0
            ? $"{dlServer}?mediaId={mediaId}&alt={resolvedFormat}"
            : $"{dlServer}?mediaId={mediaId}";

        return new ParseResult(mediaId, title, downloadUrl, resolvedFormat, formatNote);
    }

    /// <summary>Extract available format numbers from option tags. Empty set = only default (0) available.</summary>
    internal static HashSet<int> ExtractAvailableFormats(string html)
    {
        var formats = new HashSet<int>();
        foreach (Match m in FormatOptionRegex().Matches(html))
        {
            if (int.TryParse(m.Groups[1].Value, out var f))
                formats.Add(f);
        }
        return formats;
    }

    /// <summary>
    /// Resolve format with fallback. Returns (resolvedFormat, note).
    /// Note is null if preferred format was used, otherwise describes fallback.
    /// </summary>
    internal static (int Format, string? Note) ResolveFormat(int preferred, HashSet<int> available)
    {
        // No format options on page = only default available
        if (available.Count == 0)
        {
            if (preferred == 0) return (0, null);
            return (0, $"Format {preferred} not available, using JB Folder");
        }

        // Preferred format is available
        if (available.Contains(preferred))
            return (preferred, null);

        // Fallback to 0 (JB Folder / default)
        if (available.Contains(0) || available.Count == 0)
            return (0, $"Format {preferred} not available, falling back to JB Folder");

        // Use whatever is available (first option)
        var fallback = available.First();
        return (fallback, $"Format {preferred} not available, using format {fallback}");
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
