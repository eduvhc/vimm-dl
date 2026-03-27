namespace Module.Core;

/// <summary>
/// File extension helpers shared across all modules.
/// </summary>
public static class FileExtensions
{
    public const string DecIso = ".dec.iso";
    public const string Iso = ".iso";
    public const string SevenZip = ".7z";
    public const string Zip = ".zip";
    public const string Rar = ".rar";

    private static readonly string[] ArchiveExts = [SevenZip, Zip, Rar];

    public static bool IsArchive(string filename) =>
        ArchiveExts.Any(e => filename.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    public static bool IsDecIso(string filename) =>
        filename.EndsWith(DecIso, StringComparison.OrdinalIgnoreCase);

    public static bool IsIso(string filename) =>
        filename.EndsWith(Iso, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Known platform identifiers from Vimm's Lair.
/// Add new platforms here as support is added.
/// </summary>
public static class Platforms
{
    public const string PS3 = "PlayStation 3";
    public const string PS2 = "PlayStation 2";
    public const string PSP = "PlayStation Portable";
    public const string Wii = "Wii";
    public const string WiiU = "Wii U";
    public const string GameCube = "GameCube";
    public const string Xbox360 = "Xbox 360";

    public static bool IsPS3(string? platform) =>
        PS3.Equals(platform, StringComparison.OrdinalIgnoreCase);
}
