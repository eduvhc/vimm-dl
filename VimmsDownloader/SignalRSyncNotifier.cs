using Microsoft.AspNetCore.SignalR;
using Module.Sync.Bridge;

class SignalRSyncBridge(IHubContext<DownloadHub> hub) : ISyncBridge
{
    public Task SendAsync(SyncEvent evt) => evt switch
    {
        SyncProgressEvent progress => hub.Clients.All.SendAsync("SyncProgress", progress),
        SyncCompletedEvent completed => hub.Clients.All.SendAsync("SyncCompleted", completed),
        _ => Task.CompletedTask
    };
}
