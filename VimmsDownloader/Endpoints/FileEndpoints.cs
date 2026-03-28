using Module.Core.Pipeline;

static class FileEndpoints
{
    public static void MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/data", async (QueueRepository repo, DownloadQueue queue) =>
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

            var history = dbItems.Select(item =>
            {
                diskFiles.TryGetValue(item.Filename, out var archiveInfo);
                var fileExists = archiveInfo?.Exists ?? false;
                long? fileSize = fileExists ? archiveInfo!.Length : null;

                var pipeline = queue.GetPipeline(item.Platform);
                var trace = BuildTrace(item, fileExists, diskFiles, pipeline);

                return new HistoryItem(item.Id, item.Url, item.Filename,
                    archiveInfo?.FullName ?? item.Filepath,
                    item.Title, item.Platform, item.Size,
                    fileExists, fileSize,
                    trace,
                    item.CompletedAt, item.Format);
            }).ToList();

            return new DataResponse(await repo.GetQueuedItemsAsync(), history,
                queue.IsRunning, queue.IsPaused, queue.CurrentFile, queue.CurrentUrl,
                queue.CurrentProgress, queue.TotalBytes, queue.DownloadedBytes);
        });
    }

    private static PipelineTrace? BuildTrace(CompletedItem item, bool fileExists,
        Dictionary<string, FileInfo> diskFiles, IPipeline? pipeline)
    {
        if (pipeline == null) return null;

        // Determine current phase — active overlay or DB terminal state
        var phase = item.ConvPhase;
        var message = item.ConvMessage;
        string? isoFilename = item.IsoFilename;

        var activeStatuses = pipeline.GetStatuses()
            .Where(s => PipelinePhase.IsActive(s.Phase))
            .ToDictionary(s => s.ItemName, StringComparer.OrdinalIgnoreCase);

        if (activeStatuses.TryGetValue(item.Filename, out var active))
        {
            phase = active.Phase;
            message = active.Message;
            if (active.OutputFilename != null)
                isoFilename = active.OutputFilename;
        }

        // Delegate to pipeline — it defines its own step order and status mapping
        var flow = pipeline.BuildFlow(phase, message, fileExists);
        if (flow == null) return null;

        // Enrich flow steps with timing from pipeline's in-memory state
        var durations = pipeline.GetStepDurations(item.Filename);
        var traceSteps = flow.Steps.Select(s =>
        {
            var durationMs = s.DurationMs; // pipeline may have set it
            if (durationMs == null)
            {
                // Map step name to phase for duration lookup
                var phaseKey = s.Status == "active" || s.Status == "done" || s.Status == "error"
                    ? MapStepToPhase(s.Name, s.Status) : null;
                if (phaseKey != null && durations.TryGetValue(phaseKey, out var ms))
                    durationMs = ms;
            }
            return new TraceStep(s.Name, s.Status, s.Message, durationMs);
        }).ToList();

        return new PipelineTrace(flow.PipelineType, traceSteps, isoFilename,
            GetIsoSize(isoFilename, diskFiles), flow.Actions);
    }

    /// <summary>
    /// Map a trace step name to the pipeline phase it represents, for duration lookup.
    /// </summary>
    private static string? MapStepToPhase(string stepName, string status) => stepName switch
    {
        "Extract" => status == "active" ? "extracting" : status == "done" ? "extracting" : null,
        "Convert" => status == "active" ? "converting" : status == "done" ? "converting" : null,
        "Rename" => status == "active" ? "converting" : status == "done" ? "converting" : null,
        _ => null,
    };

    private static long? GetIsoSize(string? isoFilename, Dictionary<string, FileInfo> diskFiles)
    {
        if (isoFilename != null && diskFiles.TryGetValue(isoFilename, out var fi) && fi.Exists)
            return fi.Length;
        return null;
    }
}
