using Microsoft.AspNetCore.SignalR;

class DownloadHub : Hub
{
    private readonly DownloadQueue _queue;
    private readonly QueueRepository _repo;
    public DownloadHub(DownloadQueue queue, QueueRepository repo) { _queue = queue; _repo = repo; }

    public async Task StartDownload(string? downloadPath)
    {
        if (_queue.IsRunning)
        {
            _queue.Pause();
            var timeout = DateTime.UtcNow.AddSeconds(10);
            while (_queue.IsRunning && DateTime.UtcNow < timeout)
                await Task.Delay(200);
        }
        _queue.Start(downloadPath);
    }

    public async Task StartSpecific(string? downloadPath, int queueId)
    {
        _repo.MoveToFront(queueId);

        if (_queue.IsRunning)
        {
            _queue.Pause();
            var timeout = DateTime.UtcNow.AddSeconds(10);
            while (_queue.IsRunning && DateTime.UtcNow < timeout)
                await Task.Delay(200);
        }
        _queue.Start(downloadPath);
    }

    public void PauseDownload() => _queue.Pause();
    public void StopDownload() => _queue.Stop();
}
