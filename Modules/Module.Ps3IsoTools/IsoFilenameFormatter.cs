using System.Text.RegularExpressions;

namespace Module.Ps3IsoTools;

/// <summary>
/// Configurable ISO filename formatting options.
/// </summary>
public record IsoRenameOptions(
    bool FixThe = true,
    bool AddSerial = true,
    bool StripRegion = true
);

/// <summary>
/// Formats ISO filenames: fixes "The" placement, replaces region with serial.
/// "Godfather, The - The Don's Edition (Europe)" + BLES-00043
///   → "The Godfather - The Don's Edition - BLES-00043"
/// All rules are configurable via IsoRenameOptions.
/// </summary>
public static partial class IsoFilenameFormatter
{
    [GeneratedRegex(@"(\s*\([^)]*\))+\s*$")]
    private static partial Regex RegionSuffixRegex();

    [GeneratedRegex(@"^(.+?),\s*The\b")]
    private static partial Regex CommaTheRegex();

    /// <summary>
    /// Formats an ISO filename with proper title and serial.
    /// </summary>
    public static string Format(string originalFilename, string? serial, IsoRenameOptions? options = null)
    {
        var opts = options ?? new IsoRenameOptions();

        // Strip extension (.dec.iso, .iso, .7z, etc.)
        var name = originalFilename;
        if (Core.FileExtensions.IsDecIso(name))
            name = name[..^Core.FileExtensions.DecIso.Length];
        else if (Core.FileExtensions.IsIso(name))
            name = name[..^Core.FileExtensions.Iso.Length];
        else
        {
            var ext = Path.GetExtension(name);
            if (!string.IsNullOrEmpty(ext))
                name = name[..^ext.Length];
        }

        if (opts.StripRegion)
            name = RegionSuffixRegex().Replace(name, "").Trim();

        if (opts.FixThe)
            name = FixThePrefix(name);

        if (opts.AddSerial && !string.IsNullOrEmpty(serial))
            name = $"{name} - {serial}";

        name = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));

        return name + ".iso";
    }

    /// <summary>
    /// Fixes "Name, The" → "The Name" pattern from Vimm's Lair titles.
    /// </summary>
    public static string FixThePrefix(string title)
    {
        var match = CommaTheRegex().Match(title);
        if (!match.Success) return title;

        var baseName = match.Groups[1].Value.Trim();
        var rest = title[(match.Index + match.Length)..];

        return $"The {baseName}{rest}";
    }
}
