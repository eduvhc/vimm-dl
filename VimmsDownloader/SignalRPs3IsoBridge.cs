using System.Text.Json;
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
                // Serialize as JsonElement so SignalR sends the correct camelCase properties.
                // Without this, SendAsync receives 'object' and source-gen may not resolve the type.
                var json = JsonSerializer.SerializeToElement(status, AppJsonContext.Default.Ps3IsoStatusEvent);
                await hub.Clients.All.SendAsync("ConvertStatus", json);
                await hub.Clients.All.SendAsync("Status", $"[PS3] {status.ZipName}: {status.Message}");
            }
            catch { }
        }
    }
}
