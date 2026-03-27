using Microsoft.AspNetCore.SignalR;
using Module.Download.Bridge;

class SignalRDownloadBridge(IHubContext<DownloadHub> hub) : IDownloadBridge
{
    public async Task SendAsync(DownloadEvent evt)
    {
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
}
