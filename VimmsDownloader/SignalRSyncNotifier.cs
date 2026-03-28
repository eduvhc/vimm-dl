using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Module.Sync.Bridge;

class SignalRSyncBridge(IHubContext<DownloadHub> hub, QueueRepository repo) : ISyncBridge
{
    public async Task SendAsync(SyncEvent evt)
    {
        // 1. Append to events table
        try
        {
            var (itemName, eventType, message, data) = evt switch
            {
                SyncProgressEvent e => (e.Filename, "sync_progress", $"{e.Percent:F2}%",
                    $"{{\"percent\":{e.Percent:F2},\"copied\":{e.Copied},\"total\":{e.Total}}}"),
                SyncCompletedEvent e => (e.Filename, "sync_completed",
                    e.Success ? "Copied" : $"Failed: {e.Error}",
                    !e.Success && e.Error != null ? $"{{\"error\":\"{EscapeJson(e.Error)}\"}}" : null),
                _ => ("_unknown", "unknown", "", (string?)null)
            };
            await repo.AppendEventAsync(itemName, eventType, null, message, data);
        }
        catch { }

        // 2. SignalR broadcast
        try
        {
            switch (evt)
            {
                case SyncProgressEvent e:
                    await hub.Clients.All.SendAsync("SyncProgress",
                        JsonSerializer.SerializeToElement(e, AppJsonContext.Default.SyncProgressEvent));
                    break;
                case SyncCompletedEvent e:
                    await hub.Clients.All.SendAsync("SyncCompleted",
                        JsonSerializer.SerializeToElement(e, AppJsonContext.Default.SyncCompletedEvent));
                    break;
            }
        }
        catch { }
    }

    private static string EscapeJson(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
