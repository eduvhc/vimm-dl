using Microsoft.Extensions.Logging;
using Module.Core;
using Module.Core.Pipeline;
using Module.Extractor;
using Module.Ps3IsoTools;

namespace Module.Ps3Pipeline;

/// <summary>
/// PS3 .dec.iso pipeline: handles format > 0 downloads.
/// - Naked .dec.iso files → rename to .iso
/// - Archives containing .dec.iso → extract + rename
/// </summary>
public class Ps3DecIsoPipeline
{
    private readonly PipelineState _state;

    public Ps3DecIsoPipeline(PipelineState state) => _state = state;

    public async Task RenameDecIsoAsync(string filePath, string? serial = null, IsoRenameOptions? renameOptions = null)
    {
        var filename = Path.GetFileName(filePath);
        if (!FileExtensions.IsDecIso(filename))
        {
            _state.Log.LogWarning("RenameDecIso called on non-.dec.iso file: {File}", filename);
            return;
        }

        var newName = IsoFilenameFormatter.Format(filename, serial, renameOptions);
        var dir = Path.GetDirectoryName(filePath)!;
        var newPath = Path.Combine(dir, newName);

        await _state.EmitStatus(filename, Ps3Phase.Converting, "Renaming .dec.iso to .iso...");

        try
        {
            if (File.Exists(newPath)) File.Delete(newPath);
            File.Move(filePath, newPath);

            await _state.EmitStatus(filename, Ps3Phase.Done, $"ISO ready: {newName}", newName);
            _state.AddToConvertedList(filename);
            _state.Log.LogInformation("Renamed {Old} -> {New}", filename, newName);
        }
        catch (Exception ex)
        {
            await _state.EmitStatus(filename, Ps3Phase.Error, $"Rename failed: {ex.Message}");
            _state.Log.LogError(ex, "Failed to rename {File}", filename);
        }
    }

    public async Task ExtractAndRenameDecIsoAsync(string archivePath, string completedDir, string tempBaseDir,
        string? serial = null, IsoRenameOptions? renameOptions = null)
    {
        var archiveName = Path.GetFileName(archivePath);
        var tempDir = Path.Combine(tempBaseDir, Guid.NewGuid().ToString("N"));

        try
        {
            await _state.EmitStatus(archiveName, Ps3Phase.Extracting, "Checking archive\u2026");
            var (headerOk, headerErr) = await ZipExtract.QuickCheckAsync(archivePath);
            if (!headerOk)
            {
                await _state.EmitStatus(archiveName, Ps3Phase.Error, $"Archive corrupted: {headerErr}");
                return;
            }

            Directory.CreateDirectory(tempBaseDir);
            await _state.EmitStatus(archiveName, Ps3Phase.Extracting, "Extracting 0%");

            var (ok, error) = await ZipExtract.ExtractAsync(archivePath, tempDir,
                onProgress: pct => _state.EmitStatus(archiveName, Ps3Phase.Extracting, $"Extracting {pct}%").GetAwaiter().GetResult());

            if (!ok)
            {
                await _state.EmitStatus(archiveName, Ps3Phase.Error, $"Extraction failed: {error}");
                return;
            }

            if (!await HandleExtractedArchive(archiveName, tempDir, completedDir, serial, renameOptions))
            {
                await _state.EmitStatus(archiveName, Ps3Phase.Error, "No .dec.iso or .iso found in archive");
                return;
            }

            try { if (File.Exists(archivePath)) File.Delete(archivePath); } catch { }
        }
        catch (Exception ex)
        {
            await _state.EmitStatus(archiveName, Ps3Phase.Error, $"Extract+rename failed: {ex.Message}");
            _state.Log.LogError(ex, "Failed to extract and rename dec.iso from {File}", archiveName);
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    public async Task<bool> HandleExtractedArchive(string archiveName, string sourceDir, string completedDir,
        string? serial = null, IsoRenameOptions? renameOptions = null)
    {
        var decIsos = Directory.GetFiles(sourceDir, $"*{FileExtensions.DecIso}", SearchOption.AllDirectories);
        if (decIsos.Length > 0)
        {
            string? firstIsoName = null;
            foreach (var decIso in decIsos)
            {
                var isoName = IsoFilenameFormatter.Format(Path.GetFileName(decIso), serial, renameOptions);
                firstIsoName ??= isoName;
                var destPath = Path.Combine(completedDir, isoName);
                if (File.Exists(destPath)) File.Delete(destPath);
                File.Move(decIso, destPath);
                _state.Log.LogInformation("Extracted and renamed {Archive} -> {Iso}", archiveName, isoName);
            }
            await _state.EmitStatus(archiveName, Ps3Phase.Done, $"ISO ready: {firstIsoName}", firstIsoName);
            _state.AddToConvertedList(archiveName);
            return true;
        }

        var isos = Directory.GetFiles(sourceDir, $"*{FileExtensions.Iso}", SearchOption.AllDirectories);
        if (isos.Length > 0)
        {
            string? firstIsoName = null;
            foreach (var iso in isos)
            {
                var isoName = serial != null
                    ? IsoFilenameFormatter.Format(Path.GetFileName(iso), serial, renameOptions)
                    : Path.GetFileName(iso);
                firstIsoName ??= isoName;
                var destPath = Path.Combine(completedDir, isoName);
                if (File.Exists(destPath)) File.Delete(destPath);
                File.Move(iso, destPath);
                _state.Log.LogInformation("Extracted ISO from archive: {Archive} -> {Iso}", archiveName, isoName);
            }
            await _state.EmitStatus(archiveName, Ps3Phase.Done, $"ISO ready: {firstIsoName}", firstIsoName);
            _state.AddToConvertedList(archiveName);
            return true;
        }

        return false;
    }
}
