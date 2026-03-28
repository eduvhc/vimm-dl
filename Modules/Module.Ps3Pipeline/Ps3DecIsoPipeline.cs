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

        var moveResult = FileOps.TryMove(filePath, newPath);
        if (moveResult.IsOk)
        {
            await _state.EmitStatus(filename, Ps3Phase.Done, $"ISO ready: {newName}", newName);
            _state.AddToConvertedList(filename);
            _state.Log.LogInformation("Renamed {Old} -> {New}", filename, newName);
        }
        else
        {
            await _state.EmitStatus(filename, Ps3Phase.Error, $"Rename failed: {moveResult.Error}");
            _state.Log.LogError("Failed to rename {File}: {Error}", filename, moveResult.Error);
        }
    }

    public async Task ExtractAndRenameDecIsoAsync(string archivePath, string completedDir, string tempBaseDir,
        string? serial = null, IsoRenameOptions? renameOptions = null, bool deleteArchive = false)
    {
        var archiveName = Path.GetFileName(archivePath);
        var tempDir = Path.Combine(tempBaseDir, Guid.NewGuid().ToString("N"));

        try
        {
            await _state.EmitStatus(archiveName, Ps3Phase.Extracting, "Checking archive\u2026");
            var headerCheck = await ZipExtract.QuickCheckAsync(archivePath);
            if (!headerCheck.IsOk)
            {
                await _state.EmitStatus(archiveName, Ps3Phase.Error, $"Archive corrupted: {headerCheck.Error}");
                return;
            }

            Directory.CreateDirectory(tempBaseDir);
            await _state.EmitStatus(archiveName, Ps3Phase.Extracting, "Extracting 0%");

            var extractResult = await ZipExtract.ExtractAsync(archivePath, tempDir,
                onProgress: pct => _state.EmitStatus(archiveName, Ps3Phase.Extracting, $"Extracting {pct}%").GetAwaiter().GetResult());

            if (!extractResult.IsOk)
            {
                await _state.EmitStatus(archiveName, Ps3Phase.Error, $"Extraction failed: {extractResult.Error}");
                return;
            }

            var handleResult = await HandleExtractedArchive(archiveName, tempDir, completedDir, serial, renameOptions);
            if (!handleResult.IsOk)
            {
                await _state.EmitStatus(archiveName, Ps3Phase.Error, "No .dec.iso or .iso found in archive");
                return;
            }

            if (deleteArchive)
                FileOps.TryDelete(archivePath);
        }
        catch (OperationCanceledException)
        {
            await _state.EmitStatus(archiveName, Ps3Phase.Error, "Aborted by user");
        }
        finally
        {
            FileOps.TryDeleteDirectory(tempDir);
        }
    }

    public async Task<Result<string?>> HandleExtractedArchive(string archiveName, string sourceDir, string completedDir,
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
                var moveResult = FileOps.TryMove(decIso, destPath);
                if (!moveResult.IsOk)
                    return Result<string?>.Fail($"Failed to move {isoName}: {moveResult.Error}");
                _state.Log.LogInformation("Extracted and renamed {Archive} -> {Iso}", archiveName, isoName);
            }
            await _state.EmitStatus(archiveName, Ps3Phase.Done, $"ISO ready: {firstIsoName}", firstIsoName);
            _state.AddToConvertedList(archiveName);
            return Result<string?>.Ok(firstIsoName);
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
                var moveResult = FileOps.TryMove(iso, destPath);
                if (!moveResult.IsOk)
                    return Result<string?>.Fail($"Failed to move {isoName}: {moveResult.Error}");
                _state.Log.LogInformation("Extracted ISO from archive: {Archive} -> {Iso}", archiveName, isoName);
            }
            await _state.EmitStatus(archiveName, Ps3Phase.Done, $"ISO ready: {firstIsoName}", firstIsoName);
            _state.AddToConvertedList(archiveName);
            return Result<string?>.Ok(firstIsoName);
        }

        return Result<string?>.Fail("No .dec.iso or .iso found");
    }
}
