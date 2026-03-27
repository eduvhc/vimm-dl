using Microsoft.Extensions.Logging.Abstractions;
using Module.Core.Testing;

namespace Module.Sync.Tests.Helpers;

/// <summary>
/// Base class for Sync module integration tests.
/// Provides real temp directories and a configured SyncService per test.
/// </summary>
public abstract class SyncTestBase
{
    protected string SourceDir { get; private set; } = null!;
    protected string TargetDir { get; private set; } = null!;
    protected string BaseDir { get; private set; } = null!;
    protected FakeSyncBridge Bridge { get; private set; } = null!;
    protected SyncService Service { get; private set; } = null!;

    private TempDirectory _tmp = null!;

    [TestInitialize]
    public void BaseSetup()
    {
        _tmp = new TempDirectory("SyncTests");
        BaseDir = _tmp.CreateSubDir("downloads");
        SourceDir = _tmp.CreateSubDir("downloads/completed");
        TargetDir = _tmp.CreateSubDir("target");

        Bridge = new FakeSyncBridge();
        Service = CreateService(BaseDir, TargetDir);
    }

    [TestCleanup]
    public void BaseCleanup() => _tmp.Dispose();

    protected SyncService CreateService(string? downloadPath = null, string? syncPath = null)
    {
        var svc = new SyncService(Bridge, NullLogger<SyncService>.Instance);
        svc.Configure(downloadPath ?? BaseDir, syncPath ?? TargetDir);
        return svc;
    }

    protected static void CreateFile(string dir, string name, long sizeBytes = 1024)
        => TempDirectory.CreateFile(dir, name, sizeBytes);
}
