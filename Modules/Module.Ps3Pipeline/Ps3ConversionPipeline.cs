using Microsoft.Extensions.Logging;
using Module.Core.Pipeline;
using Module.Ps3IsoTools;
using Module.Ps3Pipeline.Bridge;

namespace Module.Ps3Pipeline;

/// <summary>
/// Facade over both PS3 pipelines. Implements IPipeline for generic host access.
/// </summary>
public class Ps3ConversionPipeline : IPipeline
{
    private readonly PipelineState _state;

    public Ps3JbFolderPipeline JbFolder { get; }
    public Ps3DecIsoPipeline DecIso { get; }

    public Ps3ConversionPipeline(IPs3PipelineBridge bridge, ILogger<Ps3ConversionPipeline> log)
    {
        _state = new PipelineState(bridge, log);
        DecIso = new Ps3DecIsoPipeline(_state);
        JbFolder = new Ps3JbFolderPipeline(_state, DecIso);
    }

    // JB Folder
    public void Configure(int maxParallelism) => JbFolder.Configure(maxParallelism);
    public void CleanupOrphans(string downloadBasePath) => JbFolder.CleanupOrphans(downloadBasePath);
    public bool Enqueue(string zipPath, string completedDir, string tempBaseDir, bool force = false)
        => JbFolder.Enqueue(zipPath, completedDir, tempBaseDir, force);

    // Dec ISO
    public Task RenameDecIsoAsync(string filePath, string? serial = null, IsoRenameOptions? renameOptions = null)
        => DecIso.RenameDecIsoAsync(filePath, serial, renameOptions);
    public Task ExtractAndRenameDecIsoAsync(string archivePath, string completedDir, string tempBaseDir,
        string? serial = null, IsoRenameOptions? renameOptions = null, bool deleteArchive = false)
        => DecIso.ExtractAndRenameDecIsoAsync(archivePath, completedDir, tempBaseDir, serial, renameOptions, deleteArchive);

    // State seeding
    public void SeedConverted(IEnumerable<string> names) => _state.SeedConverted(names);

    // IPipeline
    public List<PipelineStatusEvent> GetStatuses() => _state.GetStatuses();
    public bool Abort(string itemName) => _state.Abort(itemName);
    public bool IsConverted(string itemName) => _state.IsConverted(itemName);
    public void MarkConverted(string itemName) => _state.MarkConverted(itemName);
    public Dictionary<string, long> GetStepDurations(string itemName) => _state.GetStepDurations(itemName);

    // --- Flow / trace building ---

    public PipelineFlowInfo? BuildFlow(string? phase, string? message, bool fileExists)
    {
        if (phase == null)
        {
            if (!fileExists) return null;
            return new PipelineFlowInfo("none", [], ["convert", "mark-done"]);
        }

        // Infer pipeline variant from phase/message
        var isJbFolder = phase == Ps3Phase.Extracted
            || (phase == Ps3Phase.Converting && message?.Contains("Creating ISO") == true)
            || (phase == PipelinePhase.Done && message?.Contains("ISO ready") == true)
            || (phase == PipelinePhase.Error && message != null &&
                (message.Contains("Conversion failed") || message.Contains("Convert error") || message.Contains("makeps3iso")));

        var isDecIsoRename = message?.Contains(".dec.iso") == true;

        // Single-step pipeline (naked .dec.iso rename — no extract)
        if (isDecIsoRename && phase != Ps3Phase.Extracting)
        {
            var renameStatus = MapStatus(phase, isSecondStep: true);
            var steps = new List<PipelineFlowStep> { new("Rename", renameStatus, message) };
            return new PipelineFlowInfo("dec_iso", steps, BuildActions(phase, fileExists));
        }

        // Two-step pipeline
        var secondStepName = isDecIsoRename ? "Rename" : "Convert";
        var pipelineType = isDecIsoRename ? "dec_iso" : (isJbFolder ? "jb_folder" : "dec_iso_archive");

        var (extractStatus, convertStatus) = MapTwoStepStatuses(phase);
        var extractMsg = extractStatus == "active" ? message : null;
        var convertMsg = convertStatus is "active" or "error" or "done" ? message : null;

        var traceSteps = new List<PipelineFlowStep>
        {
            new("Extract", extractStatus, extractMsg),
            new(secondStepName, convertStatus, convertMsg),
        };

        return new PipelineFlowInfo(pipelineType, traceSteps, BuildActions(phase, fileExists));
    }

    private static (string ExtractStatus, string ConvertStatus) MapTwoStepStatuses(string? phase) => phase switch
    {
        Ps3Phase.Queued => ("pending", "pending"),
        Ps3Phase.Extracting => ("active", "pending"),
        Ps3Phase.Extracted => ("done", "pending"),
        Ps3Phase.Converting => ("done", "active"),
        PipelinePhase.Done => ("done", "done"),
        PipelinePhase.Skipped => ("skipped", "skipped"),
        PipelinePhase.Error => ("done", "error"),
        _ => ("pending", "pending"),
    };

    private static string MapStatus(string? phase, bool isSecondStep) => phase switch
    {
        Ps3Phase.Queued => "pending",
        Ps3Phase.Extracting => isSecondStep ? "pending" : "active",
        Ps3Phase.Extracted => isSecondStep ? "pending" : "done",
        Ps3Phase.Converting => isSecondStep ? "active" : "done",
        PipelinePhase.Done => "done",
        PipelinePhase.Error => "error",
        PipelinePhase.Skipped => "skipped",
        _ => "pending",
    };

    private static List<string> BuildActions(string? phase, bool fileExists)
    {
        if (phase == null && fileExists) return ["convert", "mark-done"];
        if (PipelinePhase.IsActive(phase)) return ["abort"];
        if (phase == PipelinePhase.Error) return ["retry"];
        return [];
    }

    // --- Duplicate checking ---

    public DuplicateCheckResult? CheckDuplicate(string completedDir, string? filename, string? isoFilename, string? convPhase)
    {
        // Active conversion — always block (pipeline has state in ps3_temp/)
        if (PipelinePhase.IsActive(convPhase))
        {
            var archiveOnDisk = filename != null && File.Exists(Path.Combine(completedDir, filename));
            return new DuplicateCheckResult("Already downloaded (conversion in progress)", archiveOnDisk, false);
        }

        // Terminal state — check filesystem
        var archiveExists = filename != null && File.Exists(Path.Combine(completedDir, filename));
        var isoExists = isoFilename != null && File.Exists(Path.Combine(completedDir, isoFilename));

        // No files on disk → not a real duplicate
        if (!archiveExists && !isoExists) return null;

        var reason = BuildDuplicateReason(convPhase, archiveExists, isoExists);
        return new DuplicateCheckResult(reason, archiveExists, isoExists);
    }

    private static string BuildDuplicateReason(string? convPhase, bool archiveExists, bool isoExists)
    {
        if (convPhase == "done" && isoExists)
            return archiveExists ? "Already converted to ISO (archive + ISO on disk)" : "Already converted to ISO";
        if (convPhase == "error")
            return archiveExists ? "Already downloaded (conversion failed)" : "Already downloaded (conversion failed, archive missing)";
        if (archiveExists)
            return "Already downloaded";
        if (isoExists)
            return "ISO already exists on disk";
        return "Already downloaded";
    }
}
