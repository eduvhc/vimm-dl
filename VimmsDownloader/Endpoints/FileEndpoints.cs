using Module.Core.Pipeline;
using Module.Ps3Pipeline;

static class FileEndpoints
{
    public static void MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        // Merged: /api/data + /api/status + /api/partials into one endpoint
        app.MapGet("/api/data", (QueueRepository repo, DownloadQueue queue, Ps3ConversionPipeline pipeline) =>
        {
            var completedDir = Path.Combine(queue.GetBasePath(), "completed");

            var dbItems = repo.GetCompletedItemsEnriched();
            dbItems.RemoveAll(i => !PathHelpers.IsArchive(i.Filename));
            var dbFilenames = new HashSet<string>(dbItems.Select(i => i.Filename), StringComparer.OrdinalIgnoreCase);

            var diskFiles = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(completedDir))
            {
                foreach (var f in Directory.GetFiles(completedDir))
                {
                    var fi = new FileInfo(f);
                    diskFiles[fi.Name] = fi;
                }
            }

            var nextId = dbItems.Count > 0 ? dbItems.Max(i => i.Id) + 1 : 1;
            foreach (var (name, fi) in diskFiles)
            {
                if (name.StartsWith('.') || !PathHelpers.IsArchive(name)) continue;
                if (!dbFilenames.Contains(name))
                    dbItems.Add(new CompletedItem(nextId++, "", name, fi.FullName));
            }

            var convStatuses = pipeline.GetStatuses()
                .ToDictionary(s => s.ItemName, StringComparer.OrdinalIgnoreCase);

            var history = dbItems.Select(item =>
            {
                diskFiles.TryGetValue(item.Filename, out var archiveInfo);
                var fileExists = archiveInfo?.Exists ?? false;
                long? fileSize = fileExists ? archiveInfo!.Length : null;

                convStatuses.TryGetValue(item.Filename, out var conv);
                var convPhase = conv?.Phase;
                var convMessage = conv?.Message;

                string? isoFilename = null;
                bool isoExists = false;
                long? isoSize = null;

                if (conv?.OutputFilename != null && diskFiles.TryGetValue(conv.OutputFilename, out var isoInfo))
                {
                    isoFilename = conv.OutputFilename;
                    isoExists = isoInfo.Exists;
                    isoSize = isoInfo.Length;
                }

                if (isoFilename == null && Module.Core.Platforms.IsPS3(item.Platform))
                {
                    if (pipeline.IsConverted(item.Filename))
                    {
                        convPhase ??= PipelinePhase.Done;
                        convMessage ??= "Previously converted";
                    }
                }

                return new HistoryItem(item.Id, item.Url, item.Filename,
                    archiveInfo?.FullName ?? item.Filepath,
                    item.Title, item.Platform, item.Size,
                    fileExists, fileSize,
                    isoFilename, isoExists, isoSize,
                    convPhase, convMessage,
                    item.CompletedAt);
            }).ToList();

            return new DataResponse(repo.GetQueuedItems(), history,
                queue.IsRunning, queue.IsPaused, queue.CurrentFile, queue.CurrentUrl,
                queue.CurrentProgress, queue.TotalBytes, queue.DownloadedBytes);
        });
    }
}
