using Module.Ps3Pipeline;

static class Ps3Endpoints
{
    public static void MapPs3Endpoints(this IEndpointRouteBuilder app)
    {
        // Merged: convert all + convert single
        app.MapPost("/api/ps3/convert", (Ps3ConvertRequest req, DownloadQueue queue, Ps3ConversionPipeline pipeline) =>
        {
            var basePath = queue.GetBasePath();
            var completedDir = Path.Combine(basePath, "completed");
            var tempBaseDir = Path.Combine(basePath, "ps3_temp");

            if (!Directory.Exists(completedDir))
                return Results.Ok(new Ps3ConvertResponse(0, 0, []));

            if (!string.IsNullOrEmpty(req.Filename))
            {
                var filepath = Path.Combine(completedDir, req.Filename);
                if (!File.Exists(filepath))
                    return Results.NotFound();
                var enqueued = pipeline.Enqueue(filepath, completedDir, tempBaseDir, force: true);
                return Results.Ok(new Ps3ConvertResponse(enqueued ? 1 : 0, enqueued ? 0 : 1, enqueued ? [req.Filename] : []));
            }

            int queued = 0, skipped = 0;
            var files = new List<string>();
            foreach (var filepath in Directory.GetFiles(completedDir))
            {
                if (!PathHelpers.IsArchive(filepath)) continue;
                if (pipeline.Enqueue(filepath, completedDir, tempBaseDir))
                { queued++; files.Add(Path.GetFileName(filepath)); }
                else skipped++;
            }
            return Results.Ok(new Ps3ConvertResponse(queued, skipped, files));
        });

        // Merged: mark-done + abort
        app.MapPost("/api/ps3/action", (Ps3ActionRequest req, Ps3ConversionPipeline pipeline) =>
        {
            bool success;
            switch (req.Action)
            {
                case "mark-done":
                    pipeline.MarkConverted(req.Filename);
                    success = true;
                    break;
                case "abort":
                    success = pipeline.Abort(req.Filename);
                    break;
                default:
                    success = false;
                    break;
            }
            return new Ps3ActionResponse(success);
        });
    }
}
