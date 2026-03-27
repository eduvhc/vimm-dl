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

        app.MapPost("/api/queue/move", (MoveRequest req, QueueRepository repo) =>
        {
            lock (QueueLock.Sync)
                return repo.MoveInQueue(req.Id, req.Direction) ? Results.Ok() : Results.NotFound();
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

        app.MapPost("/api/queue/format", (SetFormatRequest req, QueueRepository repo) =>
        {
            repo.SetFormat(req.Id, req.Format);
            return Results.Ok();
        });

        app.MapGet("/api/status", (DownloadQueue queue, QueueRepository repo) =>
        {
            var dlPath = Path.Combine(queue.GetBasePath(), "downloading");

            List<PartialFile>? partials = null;
            if (repo.HasQueuedUrls() && !queue.IsRunning && Directory.Exists(dlPath))
            {
                partials = [];
                foreach (var file in Directory.GetFiles(dlPath))
                {
                    var fi = new FileInfo(file);
                    if (fi.Length > 0)
                        partials.Add(new PartialFile(fi.Name, fi.Length, fi.Length / 1048576.0));
                }
            }

            return new StatusResponse(queue.IsRunning, queue.IsPaused, queue.CurrentFile, queue.CurrentUrl,
                queue.CurrentProgress, queue.TotalBytes, queue.DownloadedBytes, queue.GetRecentLogs(), partials);
        });
    }
}
