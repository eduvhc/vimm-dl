using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Module.Sync.Bridge;

class SignalRSyncBridge(IHubContext<DownloadHub> hub) : ISyncBridge
{
    public Task SendAsync(SyncEvent evt) => evt switch
    {
        SyncProgressEvent e => hub.Clients.All.SendAsync("SyncProgress",
            JsonSerializer.SerializeToElement(e, AppJsonContext.Default.SyncProgressEvent)),
        SyncCompletedEvent e => hub.Clients.All.SendAsync("SyncCompleted",
            JsonSerializer.SerializeToElement(e, AppJsonContext.Default.SyncCompletedEvent)),
        _ => Task.CompletedTask
    };
}
