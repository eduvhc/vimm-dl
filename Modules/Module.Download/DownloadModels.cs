namespace Module.Download;

/// <summary>An item to download from the queue.</summary>
public record DownloadItem(int Id, string Url, int Format);

/// <summary>
/// Interface for the host to provide queue items. The module doesn't
/// know about databases — it just asks for the next item and reports completion.
/// </summary>
public interface IDownloadItemProvider
{
    DownloadItem? GetNext();
    void Complete(int id, string url, string filename, string filepath);
    void Remove(int id);
}
