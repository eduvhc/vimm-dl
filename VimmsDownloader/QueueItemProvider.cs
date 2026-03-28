using Module.Download;

class QueueItemProvider(QueueRepository repo) : IDownloadItemProvider
{
    public async Task<DownloadItem?> GetNextAsync()
    {
        var row = await repo.GetNextQueueItemAsync();
        if (row == null) return null;
        var (id, url, format) = row.Value;
        return new DownloadItem(id, url, format);
    }

    public async Task CompleteAsync(int id, string url, string filename, string filepath, int format)
    {
        await repo.CompleteItemAsync(id, url, filename, filepath, format);
    }

    public async Task RemoveAsync(int id)
    {
        await repo.DeleteFromQueueAsync(id);
    }
}
