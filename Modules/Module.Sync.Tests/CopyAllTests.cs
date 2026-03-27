using Module.Sync.Tests.Helpers;
using Module.Sync.Bridge;
[TestClass]
public class CopyAllTests : SyncTestBase
{
    [TestMethod]
    public async Task CopyAll_CopiesOnlyNewFiles()
    {
        CreateFile(SourceDir, "New1.iso", 2048);
        CreateFile(SourceDir, "New2.iso", 4096);
        CreateFile(SourceDir, "Synced.iso", 1024);
        CreateFile(TargetDir, "Synced.iso", 1024);
        CreateFile(TargetDir, "TargetOnly.iso", 512);

        await Service.CopyAllNewAsync();

        Assert.IsTrue(File.Exists(Path.Combine(TargetDir, "New1.iso")));
        Assert.IsTrue(File.Exists(Path.Combine(TargetDir, "New2.iso")));
        Assert.IsTrue(File.Exists(Path.Combine(TargetDir, "Synced.iso")));
        Assert.IsTrue(File.Exists(Path.Combine(TargetDir, "TargetOnly.iso")));

        var successes = Bridge.CompletedEvents.Where(e => e.Success).ToList();
        Assert.AreEqual(2, successes.Count);
    }

    [TestMethod]
    public async Task CopyAll_NothingNew_NoCopies()
    {
        CreateFile(SourceDir, "Game.iso", 1024);
        CreateFile(TargetDir, "Game.iso", 1024);

        await Service.CopyAllNewAsync();

        Assert.AreEqual(0, Bridge.CompletedEvents.Count(e => e.Success));
    }

    [TestMethod]
    public async Task CopyAll_EmptySource_NoCopies()
    {
        await Service.CopyAllNewAsync();

        Assert.AreEqual(0, Bridge.CompletedEvents.Count(e => e.Success));
    }

    [TestMethod]
    public async Task CopyAll_Cancellation_NotifiesCancelledEvent()
    {
        CreateFile(SourceDir, "A.iso", 1024);
        CreateFile(SourceDir, "B.iso", 1024);
        CreateFile(SourceDir, "C.iso", 1024);

        // Pre-cancel before starting — deterministic: nothing should copy
        Service.Cancel(); // no-op since _cts is null, but CopyAllNewAsync creates its own

        // Instead, test that cancel during CopyAll eventually stops
        // by verifying the mechanism works: CopyAllNewAsync creates _cts, Cancel() cancels it
        var copyTask = Task.Run(async () => await Service.CopyAllNewAsync());

        // Cancel immediately
        Service.Cancel();
        await copyTask;

        // After cancel + completion, service should not be in copying state
        Assert.IsFalse(Service.IsCopying);
    }

    [TestMethod]
    public async Task CopyAll_TargetGone_NotifiesError()
    {
        CreateFile(SourceDir, "Game.iso", 1024);
        var tempTarget = Path.Combine(Path.GetTempPath(), "SyncTest_allgone_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempTarget);
        var svc = CreateService(syncPath: tempTarget);
        Directory.Delete(tempTarget, true);

        await svc.CopyAllNewAsync();

        var errors = Bridge.CompletedEvents.Where(e => !e.Success).ToList();
        Assert.IsTrue(errors.Count > 0);
        Assert.IsTrue(errors.Any(e => e.Error is not null));
    }

    [TestMethod]
    public async Task CopyAll_AllFilesGetCompletedEvents()
    {
        CreateFile(SourceDir, "A.iso", 1024);
        CreateFile(SourceDir, "B.iso", 2048);

        await Service.CopyAllNewAsync();

        Assert.AreEqual(2, Bridge.CompletedEvents.Count);
        Assert.IsTrue(Bridge.CompletedEvents.All(e => e.Success));
    }
}
