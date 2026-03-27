using Module.Ps3Iso;

static class FileEndpoints
{
    public static void MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/data", (QueueRepository repo, DownloadQueue queue, Ps3ConversionPipeline pipeline) =>
        {
            var completedDir = Path.Combine(queue.GetBasePath(), "completed");

            // Get completed items enriched with metadata
            var dbItems = repo.GetCompletedItemsEnriched();
            dbItems.RemoveAll(i => !PathHelpers.IsArchive(i.Filename));
            var dbFilenames = new HashSet<string>(dbItems.Select(i => i.Filename), StringComparer.OrdinalIgnoreCase);

            // Build file index once — avoids per-item File.Exists + FileInfo calls
            var diskFiles = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(completedDir))
            {
                foreach (var f in Directory.GetFiles(completedDir))
                {
                    var fi = new FileInfo(f);
                    diskFiles[fi.Name] = fi;
                }
            }

            // Merge archive files on disk not in DB
            var nextId = dbItems.Count > 0 ? dbItems.Max(i => i.Id) + 1 : 1;
            foreach (var (name, fi) in diskFiles)
            {
                if (name.StartsWith('.') || !PathHelpers.IsArchive(name)) continue;
                if (!dbFilenames.Contains(name))
                    dbItems.Add(new CompletedItem(nextId++, "", name, fi.FullName));
            }

            // Get conversion statuses
            var convStatuses = pipeline.GetStatuses()
                .ToDictionary(s => s.ZipName, StringComparer.OrdinalIgnoreCase);

            // Build history items
            var history = dbItems.Select(item =>
            {
                // Archive file check — lookup from index instead of per-item syscall
                diskFiles.TryGetValue(item.Filename, out var archiveInfo);
                var fileExists = archiveInfo?.Exists ?? false;
                long? fileSize = fileExists ? archiveInfo!.Length : null;

                // Conversion status
                convStatuses.TryGetValue(item.Filename, out var conv);
                var convPhase = conv?.Phase;
                var convMessage = conv?.Message;

                // Find matching ISO via structured IsoFilename field
                string? isoFilename = null;
                bool isoExists = false;
                long? isoSize = null;

                if (conv?.IsoFilename != null && diskFiles.TryGetValue(conv.IsoFilename, out var isoInfo))
                {
                    isoFilename = conv.IsoFilename;
                    isoExists = isoInfo.Exists;
                    isoSize = isoInfo.Length;
                }

                // Fallback: check converted list for items without active conversion status
                if (isoFilename == null && item.Platform != null &&
                    item.Platform.Equals("PlayStation 3", StringComparison.OrdinalIgnoreCase))
                {
                    if (pipeline.IsConverted(item.Filename))
                    {
                        convPhase ??= "done";
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

            return new DataResponse(repo.GetQueuedItems(), history);
        });

        app.MapGet("/api/partials", (DownloadQueue queue) =>
        {
            var dlPath = Path.Combine(queue.GetBasePath(), "downloading");

            if (!Directory.Exists(dlPath))
                return new PartialsResponse(null, []);

            var files = Directory.GetFiles(dlPath)
                .Select(f => new FileInfo(f))
                .Where(f => f.Length > 0)
                .Select(f => new PartialFile(f.Name, f.Length, Math.Round(f.Length / 1048576.0, 2)))
                .ToList();

            return new PartialsResponse(dlPath, files);
        });
    }
}
