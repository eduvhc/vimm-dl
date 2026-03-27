using System.Diagnostics;
using System.Text;

namespace Ps3IsoTools;

public class ConversionOptions
{
    public string Makeps3isoPath { get; set; } = "makeps3iso";
    public string Patchps3isoPath { get; set; } = "patchps3iso";
    public bool PatchFirmware { get; set; } = true;
    public string FirmwareVersion { get; set; } = "3.55";
    public bool SplitForFat32 { get; set; } = false;
    public bool RenameToGameNameId { get; set; } = true;
    public bool DeleteSourceAfter { get; set; } = false;
}

public record ConversionResult(bool Success, string? IsoPath, string? Error);

public class Ps3IsoConverter(ConversionOptions options)
{
    /// <summary>
    /// Finds the JB folder inside an extracted directory (handles nesting).
    /// Returns null if no PS3_GAME/PARAM.SFO found.
    /// </summary>
    public static string? FindJbFolder(string root)
    {
        if (File.Exists(Path.Combine(root, "PS3_GAME", "PARAM.SFO")))
            return root;

        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            if (File.Exists(Path.Combine(dir, "PS3_GAME", "PARAM.SFO")))
                return dir;

            foreach (var subdir in Directory.EnumerateDirectories(dir))
            {
                if (File.Exists(Path.Combine(subdir, "PS3_GAME", "PARAM.SFO")))
                    return subdir;
            }
        }

        return null;
    }

    /// <summary>
    /// Convert a JB folder to ISO. Folder must contain PS3_GAME/PARAM.SFO.
    /// </summary>
    public async Task<ConversionResult> ConvertFolderToIsoAsync(
        string jbFolder,
        string outputDir,
        Action<string>? onStatus = null,
        CancellationToken ct = default)
    {
        var paramSfoPath = Path.Combine(jbFolder, "PS3_GAME", "PARAM.SFO");
        if (!File.Exists(paramSfoPath))
            return new ConversionResult(false, null, $"PARAM.SFO not found at {paramSfoPath}");

        var sfo = ParamSfo.Parse(paramSfoPath);
        if (sfo == null)
            return new ConversionResult(false, null, "Failed to parse PARAM.SFO");

        onStatus?.Invoke($"Converting: {sfo.Title} [{sfo.TitleId}]");

        var isoName = options.RenameToGameNameId
            ? SanitizeFileName($"{sfo.Title} - {sfo.TitleId}")
            : SanitizeFileName(sfo.Title);
        isoName += ".iso";

        var tempIsoPath = Path.Combine(outputDir, $"temp_{Guid.NewGuid():N}.iso");
        var finalIsoPath = Path.Combine(outputDir, isoName);

        try
        {
            // makeps3iso (PS3_UPDATE is excluded by the C tool via NOPS3_UPDATE define)
            onStatus?.Invoke("Creating ISO...");
            var makeArgs = options.SplitForFat32
                ? $"-p0 -s \"{jbFolder}\" \"{tempIsoPath}\""
                : $"-p0 \"{jbFolder}\" \"{tempIsoPath}\"";

            var (makeOk, makeOutput) = await RunProcessAsync(options.Makeps3isoPath, makeArgs, ct);
            if (!makeOk || !File.Exists(tempIsoPath))
                return new ConversionResult(false, null, $"makeps3iso failed: {makeOutput}");

            // patchps3iso
            if (options.PatchFirmware)
            {
                onStatus?.Invoke($"Patching firmware to {options.FirmwareVersion}...");
                await RunProcessAsync(options.Patchps3isoPath,
                    $"-p0 \"{tempIsoPath}\" {options.FirmwareVersion}", ct);
            }

            // Rename to final name
            if (File.Exists(finalIsoPath))
                File.Delete(finalIsoPath);
            File.Move(tempIsoPath, finalIsoPath);

            var sizeMb = new FileInfo(finalIsoPath).Length / (1024.0 * 1024.0);
            onStatus?.Invoke($"ISO created: {isoName} ({sizeMb:F2} MB)");

            // Delete source folder if requested
            if (options.DeleteSourceAfter)
            {
                try { Directory.Delete(jbFolder, true); } catch { }
            }

            return new ConversionResult(true, finalIsoPath, null);
        }
        catch (Exception ex)
        {
            try { if (File.Exists(tempIsoPath)) File.Delete(tempIsoPath); } catch { }
            return new ConversionResult(false, null, ex.Message);
        }
    }

    static async Task<(bool Success, string Output)> RunProcessAsync(
        string fileName, string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {fileName}");

        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(ct);
        return (process.ExitCode == 0, output.ToString());
    }

    static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(invalid.Contains(c) ? '_' : c);
        return sb.ToString();
    }
}
