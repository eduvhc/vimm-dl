using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Module.Core.Pipeline;
using Module.Ps3Pipeline.Bridge;

class SignalRPs3PipelineBridge(IHubContext<DownloadHub> hub) : IPs3PipelineBridge
{
    public async Task SendAsync(PipelineStatusEvent evt)
    {
        try
        {
            var json = JsonSerializer.SerializeToElement(evt, AppJsonContext.Default.PipelineStatusEvent);
            await hub.Clients.All.SendAsync("ConvertStatus", json);
            await hub.Clients.All.SendAsync("Status", $"[PS3] {evt.ItemName}: {evt.Message}");
        }
        catch { }
    }
}
