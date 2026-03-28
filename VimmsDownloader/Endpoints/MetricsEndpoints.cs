using System.Text.RegularExpressions;

static partial class MetricsEndpoints
{
    [GeneratedRegex(@"([\d,.]+)\s*(GB|MB|KB)", RegexOptions.IgnoreCase)]
    private static partial Regex SizeRegex();

    public static void MapMetricsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/metrics", async (QueueRepository repo, DownloadQueue queue) =>
        {
            var dlPath = repo.GetDownloadPath();
            var completedDir = Path.Combine(dlPath, "completed");

            long diskFree = 0, diskTotal = 0;
            try
            {
                var root = Path.GetPathRoot(Path.GetFullPath(dlPath));
                if (root != null)
                {
                    var drive = new DriveInfo(root);
                    diskFree = drive.AvailableFreeSpace;
                    diskTotal = drive.TotalSize;
                }
            }
            catch { }

            // Queued total: parse size strings from url_meta
            var queued = await repo.GetQueuedItemsAsync();
            long queuedTotal = 0;
            foreach (var item in queued)
            {
                if (item.Size != null)
                    queuedTotal += ParseSizeToBytes(item.Size);
            }

            // Build set of tracked filenames: archives + ISOs from DB
            var dbCompleted = await repo.GetCompletedItemsEnrichedAsync();
            var trackedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in dbCompleted)
            {
                trackedNames.Add(c.Filename);
                if (c.IsoFilename != null) trackedNames.Add(c.IsoFilename);
            }

            long completedTotal = 0;
            int completedCount = 0;
            long orphanedTotal = 0;
            int orphanedCount = 0;

            // Scan completed/ — archives + ISOs
            if (Directory.Exists(completedDir))
            {
                foreach (var f in Directory.GetFiles(completedDir))
                {
                    var fi = new FileInfo(f);
                    if (fi.Name.StartsWith('.')) continue;
                    if (trackedNames.Contains(fi.Name))
                    {
                        completedTotal += fi.Length;
                        completedCount++;
                    }
                    else
                    {
                        orphanedTotal += fi.Length;
                        orphanedCount++;
                    }
                }
            }

            // Scan downloading/ — in-progress files
            var downloadingDir = Path.Combine(dlPath, "downloading");
            long downloadingTotal = 0;
            int downloadingCount = 0;
            if (Directory.Exists(downloadingDir))
            {
                foreach (var f in Directory.GetFiles(downloadingDir))
                {
                    var fi = new FileInfo(f);
                    downloadingTotal += fi.Length;
                    downloadingCount++;
                }
            }

            return new MetricsResponse(diskFree, diskTotal,
                queuedTotal, queued.Count,
                completedTotal, completedCount,
                orphanedTotal, orphanedCount,
                downloadingTotal, downloadingCount);
        });
    }

    internal static long ParseSizeToBytes(string size)
    {
        var m = SizeRegex().Match(size);
        if (!m.Success) return 0;
        var value = double.TryParse(m.Groups[1].Value.Replace(",", ""), out var v) ? v : 0;
        return m.Groups[2].Value.ToUpperInvariant() switch
        {
            "GB" => (long)(value * 1024 * 1024 * 1024),
            "MB" => (long)(value * 1024 * 1024),
            "KB" => (long)(value * 1024),
            _ => 0,
        };
    }
}
