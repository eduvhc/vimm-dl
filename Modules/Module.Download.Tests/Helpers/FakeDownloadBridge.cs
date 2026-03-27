using Module.Core.Testing;
using Module.Download.Bridge;

namespace Module.Download.Tests.Helpers;

public class FakeDownloadBridge : FakeBridge<DownloadEvent>, IDownloadBridge
{
    public IReadOnlyList<DownloadProgressEvent> ProgressEvents => Of<DownloadProgressEvent>();
    public IReadOnlyList<DownloadCompletedEvent> CompletedEvents => Of<DownloadCompletedEvent>();
    public IReadOnlyList<DownloadErrorEvent> ErrorEvents => Of<DownloadErrorEvent>();
    public IReadOnlyList<DownloadStatusEvent> StatusEvents => Of<DownloadStatusEvent>();
}
