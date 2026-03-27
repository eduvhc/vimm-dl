using Module.Download;

class QueueItemProvider(QueueRepository repo) : IDownloadItemProvider
{
    public DownloadItem? GetNext()
    {
        var row = repo.GetNextQueueItem();
        if (row == null) return null;
        var (id, url, format) = row.Value;
        return new DownloadItem(id, url, format);
    }

    public void Complete(int id, string url, string filename, string filepath)
    {
        lock (QueueLock.Sync)
            repo.CompleteItem(id, url, filename, filepath);
    }

    public void Remove(int id)
    {
        repo.DeleteFromQueue(id);
    }
}
