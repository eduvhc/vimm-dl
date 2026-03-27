static class Ps3Endpoints
{
    public static void MapPs3Endpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/convert-ps3", (DownloadQueue queue, Ps3ConversionPipeline pipeline) =>
        {
            var basePath = queue.GetBasePath();
            var completedDir = Path.Combine(basePath, "completed");
            var tempBaseDir = Path.Combine(basePath, "ps3_temp");

            if (!Directory.Exists(completedDir))
                return new ConvertPs3Response(0, 0, []);

            int queued = 0, skipped = 0;
            var files = new List<string>();

            foreach (var filepath in Directory.GetFiles(completedDir))
            {
                if (!PathHelpers.IsArchive(filepath))
                    continue;

                if (pipeline.Enqueue(filepath, completedDir, tempBaseDir))
                {
                    queued++;
                    files.Add(Path.GetFileName(filepath));
                }
                else skipped++;
            }

            return new ConvertPs3Response(queued, skipped, files);
        });

        app.MapPost("/api/convert-ps3/single", (ConvertSingleRequest req, DownloadQueue queue, Ps3ConversionPipeline pipeline) =>
        {
            var basePath = queue.GetBasePath();
            var completedDir = Path.Combine(basePath, "completed");
            var tempBaseDir = Path.Combine(basePath, "ps3_temp");
            var filepath = Path.Combine(completedDir, req.Filename);

            if (!File.Exists(filepath))
                return Results.NotFound();

            var enqueued = pipeline.Enqueue(filepath, completedDir, tempBaseDir, force: true);
            return Results.Ok(new ConvertSingleResponse(enqueued, req.Filename));
        });

        app.MapPost("/api/convert-ps3/mark-done", (ConvertSingleRequest req, Ps3ConversionPipeline pipeline) =>
        {
            pipeline.MarkConverted(req.Filename);
            return Results.Ok();
        });

        app.MapPost("/api/convert-ps3/abort", (ConvertSingleRequest req, Ps3ConversionPipeline pipeline) =>
        {
            var aborted = pipeline.Abort(req.Filename);
            return Results.Ok(new AbortResponse(aborted));
        });

    }
}
