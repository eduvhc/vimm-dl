using Microsoft.AspNetCore.SignalR;
using Module.Download.Bridge;

class SignalRDownloadBridge(IHubContext<DownloadHub> hub, QueueRepository repo) : IDownloadBridge
{
    public async Task SendAsync(DownloadEvent evt)
    {
        // 1. Append to events table
        try
        {
            var (itemName, eventType, message, data) = evt switch
            {
                DownloadStatusEvent e => (ExtractItemName(e.Message), "download_status", e.Message, (string?)null),
                DownloadProgressEvent e => (e.Filename, "download_progress", e.Progress,
                    $"{{\"pct\":{e.Pct:F2},\"speed\":{e.SpeedMBps:F2},\"downloaded\":{e.Downloaded},\"total\":{e.Total}}}"),
                DownloadCompletedEvent e => (e.Filename, "download_completed", $"Downloaded: {e.Filename}",
                    $"{{\"url\":\"{EscapeJson(e.Url)}\",\"filepath\":\"{EscapeJson(e.Filepath)}\"}}"),
                DownloadErrorEvent e => (ExtractItemName(e.Message), "download_error", e.Message, (string?)null),
                DownloadDoneEvent => ("_queue", "download_done", "Queue empty", (string?)null),
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
                case DownloadStatusEvent e:
                    await hub.Clients.All.SendAsync("Status", e.Message);
                    break;
                case DownloadProgressEvent e:
                    await hub.Clients.All.SendAsync("Progress", e.Progress);
                    break;
                case DownloadCompletedEvent e:
                    await hub.Clients.All.SendAsync("Completed", e);
                    break;
                case DownloadErrorEvent e:
                    await hub.Clients.All.SendAsync("Error", e.Message);
                    break;
                case DownloadDoneEvent:
                    await hub.Clients.All.SendAsync("Done", "All downloads finished.");
                    break;
            }
        }
        catch { }
    }

    private static string ExtractItemName(string message)
    {
        // Try to extract filename/URL from messages like "Processing: https://..." or "Failed: https://..."
        var colonIdx = message.IndexOf(": ", StringComparison.Ordinal);
        return colonIdx >= 0 ? message[(colonIdx + 2)..].Trim() : message;
    }

    private static string EscapeJson(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
