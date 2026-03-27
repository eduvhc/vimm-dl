static class DownloadEndpoints
{
    public static void MapDownloadEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/queue", (AddRequest req, QueueRepository repo) =>
        {
            foreach (var url in req.Urls.Take(40))
                repo.AddToQueue(url, req.Format ?? 0);
            return Results.Ok(new QueueListResponse(repo.GetQueueIds()));
        });

        app.MapDelete("/api/queue/{id:int}", (int id, QueueRepository repo) =>
        {
            lock (QueueLock.Sync)
            {
                repo.DeleteFromQueue(id);
                return Results.Ok();
            }
        });

        // Merged: move + format into PATCH
        app.MapPatch("/api/queue/{id:int}", (int id, QueuePatchRequest req, QueueRepository repo) =>
        {
            if (req.Direction != null)
            {
                lock (QueueLock.Sync)
                    return repo.MoveInQueue(id, req.Direction) ? Results.Ok() : Results.NotFound();
            }
            if (req.Format.HasValue)
            {
                repo.SetFormat(id, req.Format.Value);
                return Results.Ok();
            }
            return Results.BadRequest();
        });

        app.MapPost("/api/queue/reorder", (QueueReorderRequest req, QueueRepository repo) =>
        {
            lock (QueueLock.Sync)
            {
                repo.ReorderQueue(req.Ids);
                return Results.Ok();
            }
        });

        app.MapDelete("/api/queue", (QueueRepository repo) =>
        {
            repo.ClearQueue();
            return Results.Ok();
        });

        app.MapDelete("/api/completed/{id:int}", (int id, QueueRepository repo) =>
        {
            repo.DeleteCompleted(id);
            return Results.Ok();
        });

        app.MapGet("/api/queue/export", (QueueRepository repo) =>
        {
            var items = repo.GetQueueIds()
                .Select(q => new QueueExportItem(q.Url, q.Format))
                .ToList();
            return Results.Ok(items);
        });

        app.MapPost("/api/queue/import", (List<QueueExportItem> items, QueueRepository repo) =>
        {
            var existing = new HashSet<string>(
                repo.GetQueueIds().Select(q => q.Url), StringComparer.OrdinalIgnoreCase);

            int added = 0, skipped = 0;
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Url) || existing.Contains(item.Url))
                { skipped++; continue; }

                repo.AddToQueue(item.Url, item.Format);
                existing.Add(item.Url);
                added++;
            }
            return Results.Ok(new QueueImportResponse(added, skipped));
        });
    }
}
