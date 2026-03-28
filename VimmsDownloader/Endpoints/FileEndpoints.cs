using Module.Core.Pipeline;
using Module.Ps3Pipeline;

static class FileEndpoints
{
    public static void MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/data", async (QueueRepository repo, DownloadQueue queue, Ps3ConversionPipeline pipeline) =>
        {
            var completedDir = Path.Combine(queue.GetBasePath(), "completed");

            var dbItems = await repo.GetCompletedItemsEnrichedAsync();
            dbItems.RemoveAll(i => !PathHelpers.IsArchive(i.Filename));
            var dbFilenames = new HashSet<string>(dbItems.Select(i => i.Filename), StringComparer.OrdinalIgnoreCase);

            var diskFiles = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(completedDir))
            {
                foreach (var f in Directory.GetFiles(completedDir))
                    diskFiles[new FileInfo(f).Name] = new FileInfo(f);
            }

            var nextId = dbItems.Count > 0 ? dbItems.Max(i => i.Id) + 1 : 1;
            foreach (var (name, fi) in diskFiles)
            {
                if (name.StartsWith('.') || !PathHelpers.IsArchive(name)) continue;
                if (!dbFilenames.Contains(name))
                    dbItems.Add(new CompletedItem(nextId++, "", name, fi.FullName));
            }

            var activeStatuses = pipeline.GetStatuses()
                .Where(s => PipelinePhase.IsActive(s.Phase))
                .ToDictionary(s => s.ItemName, StringComparer.OrdinalIgnoreCase);

            var history = dbItems.Select(item =>
            {
                diskFiles.TryGetValue(item.Filename, out var archiveInfo);
                var fileExists = archiveInfo?.Exists ?? false;
                long? fileSize = fileExists ? archiveInfo!.Length : null;

                var trace = BuildTrace(item, fileExists, activeStatuses, diskFiles);

                return new HistoryItem(item.Id, item.Url, item.Filename,
                    archiveInfo?.FullName ?? item.Filepath,
                    item.Title, item.Platform, item.Size,
                    fileExists, fileSize,
                    trace,
                    item.CompletedAt);
            }).ToList();

            return new DataResponse(await repo.GetQueuedItemsAsync(), history,
                queue.IsRunning, queue.IsPaused, queue.CurrentFile, queue.CurrentUrl,
                queue.CurrentProgress, queue.TotalBytes, queue.DownloadedBytes);
        });
    }

    private static PipelineTrace? BuildTrace(CompletedItem item, bool fileExists,
        Dictionary<string, PipelineStatusEvent> activeStatuses,
        Dictionary<string, FileInfo> diskFiles)
    {
        var isPs3 = Module.Core.Platforms.IsPS3(item.Platform);
        if (!isPs3) return null;

        // Determine current phase — active overlay or DB terminal state
        var phase = item.ConvPhase;
        var message = item.ConvMessage;
        string? isoFilename = item.IsoFilename;

        if (activeStatuses.TryGetValue(item.Filename, out var active))
        {
            phase = active.Phase;
            message = active.Message;
            if (active.OutputFilename != null)
                isoFilename = active.OutputFilename;
        }

        // No conversion activity at all
        if (phase == null)
        {
            if (!fileExists) return null;
            return new PipelineTrace("none", [], null, null, ["convert", "mark-done"]);
        }

        // Determine pipeline type from the phase flow:
        // - "extracted" phase only exists in JB folder pipeline
        // - If phase went straight to "converting" without "extracted", it's dec.iso
        // We infer: if IsoFilename ends with serial pattern or phase was "extracted", it's JB folder
        // Simplest heuristic: JB folder pipeline emits "extracted" phase; dec.iso skips it
        var isJbFolder = phase == Ps3Phase.Extracted
            || (phase == Ps3Phase.Converting && message != null && message.Contains("Creating ISO"))
            || (phase == PipelinePhase.Done && message != null && message.Contains("ISO ready"))
            || (phase == PipelinePhase.Error && message != null &&
                (message.Contains("Conversion failed") || message.Contains("Convert error") || message.Contains("makeps3iso")));

        // If message mentions "Renaming .dec.iso" it's dec.iso rename
        var isDecIsoRename = message != null && message.Contains(".dec.iso");

        // Build steps based on inferred pipeline type
        var hasExtractPhase = isJbFolder || (!isDecIsoRename && phase != PipelinePhase.Skipped);
        var secondStepName = isDecIsoRename ? "Rename" : "Convert";
        var pipelineType = isDecIsoRename ? "dec_iso" : (isJbFolder ? "jb_folder" : "dec_iso_archive");

        // Single-step pipeline (naked dec.iso rename — no extract)
        if (isDecIsoRename && phase != Ps3Phase.Extracting)
        {
            var renameStatus = MapStatus(phase, isSecondStep: true);
            var steps = new List<TraceStep> { new("Rename", renameStatus, message) };
            return new PipelineTrace("dec_iso", steps, isoFilename,
                GetIsoSize(isoFilename, diskFiles), BuildActions(phase, fileExists));
        }

        // Two-step pipeline
        var (extractStatus, convertStatus) = MapTwoStepStatuses(phase);
        var extractMsg = extractStatus == "active" ? message : null;
        var convertMsg = convertStatus == "active" || convertStatus == "error" || convertStatus == "done" ? message : null;

        var traceSteps = new List<TraceStep>
        {
            new("Extract", extractStatus, extractMsg),
            new(secondStepName, convertStatus, convertMsg),
        };

        return new PipelineTrace(pipelineType, traceSteps, isoFilename,
            GetIsoSize(isoFilename, diskFiles), BuildActions(phase, fileExists));
    }

    private static (string ExtractStatus, string ConvertStatus) MapTwoStepStatuses(string? phase) => phase switch
    {
        Ps3Phase.Queued => ("pending", "pending"),
        Ps3Phase.Extracting => ("active", "pending"),
        Ps3Phase.Extracted => ("done", "pending"),
        Ps3Phase.Converting => ("done", "active"),
        PipelinePhase.Done => ("done", "done"),
        PipelinePhase.Skipped => ("skipped", "skipped"),
        PipelinePhase.Error => ("done", "error"), // assume error in second step by default
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

    private static long? GetIsoSize(string? isoFilename, Dictionary<string, FileInfo> diskFiles)
    {
        if (isoFilename != null && diskFiles.TryGetValue(isoFilename, out var fi) && fi.Exists)
            return fi.Length;
        return null;
    }
}
