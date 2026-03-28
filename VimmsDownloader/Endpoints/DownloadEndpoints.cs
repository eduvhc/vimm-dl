static class DownloadEndpoints
{
    public static void MapDownloadEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/queue", async (AddRequest req, QueueRepository repo,
            IServiceProvider services, ILogger<QueueRepository> logger) =>
        {
            var urls = req.Urls;
            foreach (var url in urls)
                await repo.AddToQueueAsync(url, req.Format ?? 0);

            if (urls.Count > 0)
                MetadataFetcher.FetchInBackground(urls, services, logger);

            return Results.Ok(new QueueListResponse(await repo.GetQueueIdsAsync()));
        });

        app.MapDelete("/api/queue/{id:int}", async (int id, QueueRepository repo) =>
        {
            await repo.DeleteFromQueueAsync(id);
            return Results.Ok();
        });

        // Merged: move + format into PATCH
        app.MapPatch("/api/queue/{id:int}", async (int id, QueuePatchRequest req, QueueRepository repo) =>
        {
            if (req.Direction != null)
                return await repo.MoveInQueueAsync(id, req.Direction) ? Results.Ok() : Results.NotFound();

            if (req.Format.HasValue)
            {
                await repo.SetFormatAsync(id, req.Format.Value);
                return Results.Ok();
            }
            return Results.BadRequest();
        });

        app.MapPost("/api/queue/reorder", async (QueueReorderRequest req, QueueRepository repo) =>
        {
            await repo.ReorderQueueAsync(req.Ids);
            return Results.Ok();
        });

        app.MapDelete("/api/queue", async (QueueRepository repo) =>
        {
            await repo.ClearQueueAsync();
            return Results.Ok();
        });

        app.MapDelete("/api/completed/{id:int}", async (int id, bool? deleteFiles, QueueRepository repo, DownloadQueue queue) =>
        {
            if (deleteFiles == true)
            {
                var item = await repo.GetCompletedByIdAsync(id);
                if (item != null)
                {
                    var (filepath, filename, isoFilename) = item.Value;
                    var completedDir = Path.Combine(queue.GetBasePath(), "completed");
                    // Delete archive
                    if (filepath != null) try { File.Delete(filepath); } catch { }
                    else if (filename != null) try { File.Delete(Path.Combine(completedDir, filename)); } catch { }
                    // Delete ISO
                    if (isoFilename != null) try { File.Delete(Path.Combine(completedDir, isoFilename)); } catch { }
                }
            }
            await repo.DeleteCompletedAsync(id);
            return Results.Ok();
        });

        app.MapGet("/api/queue/export", async (QueueRepository repo) =>
        {
            var items = (await repo.GetQueueIdsAsync())
                .Select(q => new QueueExportItem(q.Url, q.Format))
                .ToList();
            return Results.Ok(items);
        });

        app.MapPost("/api/queue/import", async (List<QueueExportItem> items, QueueRepository repo,
            IServiceProvider services, ILogger<QueueRepository> logger) =>
        {
            var existing = new HashSet<string>(
                (await repo.GetQueueIdsAsync()).Select(q => q.Url), StringComparer.OrdinalIgnoreCase);

            int added = 0, skipped = 0;
            var newUrls = new List<string>();
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Url) || existing.Contains(item.Url))
                { skipped++; continue; }

                await repo.AddToQueueAsync(item.Url, item.Format);
                existing.Add(item.Url);
                newUrls.Add(item.Url);
                added++;
            }

            if (newUrls.Count > 0)
                MetadataFetcher.FetchInBackground(newUrls, services, logger);

            return Results.Ok(new QueueImportResponse(added, skipped));
        });
    }
}
