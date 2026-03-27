using Module.Sync;

static class SyncEndpoints
{
    public static void MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/sync/compare", (SyncService sync) => Results.Ok(sync.Compare()));

        app.MapPost("/api/sync/path", (SyncSetPathRequest req, SyncService sync) =>
        {
            var path = PathHelpers.ExpandPath(req.Path);
            if (string.IsNullOrEmpty(path))
                return Results.BadRequest("Empty path");

            sync.SetSyncPath(path);
            return Results.Ok();
        });

        app.MapPost("/api/sync/copy", (SyncService sync, ILogger<SyncService> log) =>
        {
            if (sync.IsCopying)
                return Results.Conflict("A sync copy is already in progress");

            _ = Task.Run(async () =>
            {
                try { await sync.CopyAllNewAsync(); }
                catch (Exception ex) { log.LogError(ex, "Unhandled error in sync copy-all"); }
            });
            return Results.Accepted();
        });

        app.MapPost("/api/sync/copy/single", (SyncCopyRequest req, SyncService sync, ILogger<SyncService> log) =>
        {
            if (sync.IsCopying)
                return Results.Conflict("A sync copy is already in progress");

            if (string.IsNullOrWhiteSpace(req.Filename))
                return Results.BadRequest("Filename is required");

            _ = Task.Run(async () =>
            {
                try { await sync.CopyFileAsync(req.Filename); }
                catch (Exception ex) { log.LogError(ex, "Unhandled error copying {File}", req.Filename); }
            });
            return Results.Accepted();
        });

        app.MapPost("/api/sync/cancel", (SyncService sync) =>
        {
            sync.Cancel();
            return Results.Ok();
        });
    }
}
