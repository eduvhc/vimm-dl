static class EventEndpoints
{
    private const int MaxLimit = 500;

    public static void MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/events", async (int? limit, int? offset, string? type, string? item, QueueRepository repo) =>
        {
            var clampedLimit = Math.Clamp(limit ?? 200, 1, MaxLimit);
            return await repo.GetEventsAsync(clampedLimit, offset ?? 0, type, item);
        });
    }
}
