using Microsoft.AspNetCore.SignalR;
using Module.Ps3Iso.Bridge;

class SignalRPs3IsoBridge(IHubContext<DownloadHub> hub) : IPs3IsoBridge
{
    public async Task SendAsync(Ps3IsoEvent evt)
    {
        if (evt is Ps3IsoStatusEvent status)
        {
            try
            {
                await hub.Clients.All.SendAsync("ConvertStatus", status);
                await hub.Clients.All.SendAsync("Status", $"[PS3] {status.ZipName}: {status.Message}");
            }
            catch { }
        }
    }
}
