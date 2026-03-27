using Module.Sync;

static class SyncEndpoints
{
    public static void MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        // Merged: set path + compare into one POST
        app.MapPost("/api/sync/compare", (SyncCompareRequest req, SyncService sync) =>
        {
            var path = PathHelpers.ExpandPath(req.Path);
            if (string.IsNullOrEmpty(path))
                return Results.BadRequest("Empty path");

            sync.SetSyncPath(path);
            return Results.Ok(sync.Compare());
        });

        // Merged: copy all + copy single (filename=null means all)
        app.MapPost("/api/sync/copy", (SyncCopyRequest req, SyncService sync, ILogger<SyncService> log) =>
        {
            if (sync.IsCopying)
                return Results.Conflict("A sync copy is already in progress");

            if (!string.IsNullOrWhiteSpace(req.Filename))
            {
                _ = Task.Run(async () =>
                {
                    try { await sync.CopyFileAsync(req.Filename); }
                    catch (Exception ex) { log.LogError(ex, "Unhandled error copying {File}", req.Filename); }
                });
            }
            else
            {
                _ = Task.Run(async () =>
                {
                    try { await sync.CopyAllNewAsync(); }
                    catch (Exception ex) { log.LogError(ex, "Unhandled error in sync copy-all"); }
                });
            }
            return Results.Accepted();
        });

        app.MapPost("/api/sync/cancel", (SyncService sync) =>
        {
            sync.Cancel();
            return Results.Ok();
        });
    }
}
