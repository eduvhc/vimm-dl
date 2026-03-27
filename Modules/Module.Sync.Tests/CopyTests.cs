using Module.Sync.Tests.Helpers;
using Module.Sync.Bridge;
[TestClass]
public class CopyTests : SyncTestBase
{
    // --- Successful copy ---

    [TestMethod]
    public async Task CopyFile_Success_FileExistsInTarget()
    {
        CreateFile(SourceDir, "Game.iso", 8192);

        await Service.CopyFileAsync("Game.iso");

        var dest = Path.Combine(TargetDir, "Game.iso");
        Assert.IsTrue(File.Exists(dest));
        Assert.AreEqual(
            new FileInfo(Path.Combine(SourceDir, "Game.iso")).Length,
            new FileInfo(dest).Length);
    }

    [TestMethod]
    public async Task CopyFile_Success_SourcePreserved()
    {
        CreateFile(SourceDir, "Game.iso", 4096);

        await Service.CopyFileAsync("Game.iso");

        Assert.IsTrue(File.Exists(Path.Combine(SourceDir, "Game.iso")));
    }

    [TestMethod]
    public async Task CopyFile_Success_ContentMatches()
    {
        var content = new byte[4096];
        Random.Shared.NextBytes(content);
        File.WriteAllBytes(Path.Combine(SourceDir, "Game.iso"), content);

        await Service.CopyFileAsync("Game.iso");

        var destContent = File.ReadAllBytes(Path.Combine(TargetDir, "Game.iso"));
        CollectionAssert.AreEqual(content, destContent);
    }

    [TestMethod]
    public async Task CopyFile_Success_NotifiesCompletion()
    {
        CreateFile(SourceDir, "Game.iso", 2048);

        await Service.CopyFileAsync("Game.iso");

        Assert.AreEqual(1, Bridge.CompletedEvents.Count);
        Assert.IsTrue(Bridge.LastCompleted!.Success);
        Assert.AreEqual("Game.iso", Bridge.LastCompleted.Filename);
        Assert.IsNull(Bridge.LastCompleted.Error);
    }

    [TestMethod]
    public async Task CopyFile_Success_ResetsState()
    {
        CreateFile(SourceDir, "Game.iso", 2048);

        await Service.CopyFileAsync("Game.iso");

        Assert.IsFalse(Service.IsCopying);
        Assert.IsNull(Service.CurrentFile);
        Assert.AreEqual(0.0, Service.CurrentProgress);
    }

    [TestMethod]
    public async Task CopyFile_Success_SendsFinalProgress100()
    {
        CreateFile(SourceDir, "Game.iso", 4096);

        await Service.CopyFileAsync("Game.iso");

        var last = Bridge.ProgressEvents[^1];
        Assert.AreEqual("Game.iso", last.Filename);
        Assert.AreEqual(100.0, last.Percent);
        Assert.AreEqual(last.Total, last.Copied);
    }

    // --- Pre-flight failures ---

    [TestMethod]
    public async Task CopyFile_SyncPathEmpty_NotifiesError()
    {
        var svc = CreateService(syncPath: "");
        CreateFile(SourceDir, "Game.iso", 1024);

        await svc.CopyFileAsync("Game.iso");

        Assert.AreEqual(1, Bridge.CompletedEvents.Count);
        Assert.IsFalse(Bridge.LastCompleted!.Success);
        StringAssert.Contains(Bridge.LastCompleted.Error, "not configured");
    }

    [TestMethod]
    public async Task CopyFile_TargetPathGone_NotifiesError()
    {
        CreateFile(SourceDir, "Game.iso", 1024);
        var tempTarget = Path.Combine(Path.GetTempPath(), "SyncTest_gone_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempTarget);
        var svc = CreateService(syncPath: tempTarget);
        Directory.Delete(tempTarget, true);

        await svc.CopyFileAsync("Game.iso");

        Assert.IsFalse(Bridge.LastCompleted!.Success);
        StringAssert.Contains(Bridge.LastCompleted.Error, "not accessible");
    }

    [TestMethod]
    public async Task CopyFile_SourceFileNotFound_NotifiesError()
    {
        await Service.CopyFileAsync("DoesNotExist.iso");

        Assert.IsFalse(Bridge.LastCompleted!.Success);
        StringAssert.Contains(Bridge.LastCompleted.Error, "not found");
    }

    [TestMethod]
    public async Task CopyFile_NoPartialFileLeftOnSourceMissing()
    {
        await Service.CopyFileAsync("Missing.iso");

        Assert.IsFalse(File.Exists(Path.Combine(TargetDir, "Missing.iso")));
    }

    // --- Cancellation ---

    [TestMethod]
    public async Task CopyFile_Cancellation_CleansUpPartialFile()
    {
        CreateFile(SourceDir, "Big.iso", 1024 * 1024 * 5);

        // Cancel immediately — before copy starts writing
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Service.CopyFileAsync("Big.iso", cts.Token);

        Assert.IsFalse(File.Exists(Path.Combine(TargetDir, "Big.iso")),
            "Partial file should be deleted after cancellation");

        var completed = Bridge.CompletedEvents.FirstOrDefault(e => e.Filename == "Big.iso");
        Assert.IsNotNull(completed);
        Assert.IsFalse(completed.Success);
        StringAssert.Contains(completed.Error, "Cancelled");
    }

    [TestMethod]
    public async Task CopyFile_Cancellation_ResetsState()
    {
        CreateFile(SourceDir, "Big.iso", 1024 * 1024 * 5);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Service.CopyFileAsync("Big.iso", cts.Token);

        Assert.IsFalse(Service.IsCopying);
        Assert.IsNull(Service.CurrentFile);
    }

    // --- Multiple copies ---

    [TestMethod]
    public async Task CopyFile_MultipleCopies_AllSucceed()
    {
        CreateFile(SourceDir, "A.iso", 2048);
        CreateFile(SourceDir, "B.iso", 4096);
        CreateFile(SourceDir, "C.iso", 1024);

        await Service.CopyFileAsync("A.iso");
        await Service.CopyFileAsync("B.iso");
        await Service.CopyFileAsync("C.iso");

        Assert.IsTrue(File.Exists(Path.Combine(TargetDir, "A.iso")));
        Assert.IsTrue(File.Exists(Path.Combine(TargetDir, "B.iso")));
        Assert.IsTrue(File.Exists(Path.Combine(TargetDir, "C.iso")));
        Assert.AreEqual(3, Bridge.CompletedEvents.Count(e => e.Success));
    }

    [TestMethod]
    public async Task CopyFile_OverwritesExistingTarget()
    {
        var sourceContent = new byte[2048];
        Random.Shared.NextBytes(sourceContent);
        File.WriteAllBytes(Path.Combine(SourceDir, "Game.iso"), sourceContent);
        File.WriteAllBytes(Path.Combine(TargetDir, "Game.iso"), new byte[512]);

        await Service.CopyFileAsync("Game.iso");

        var destContent = File.ReadAllBytes(Path.Combine(TargetDir, "Game.iso"));
        CollectionAssert.AreEqual(sourceContent, destContent);
    }

    [TestMethod]
    public async Task CopyFile_AfterFailure_NextCopyWorks()
    {
        await Service.CopyFileAsync("DoesNotExist.iso");
        Assert.IsFalse(Bridge.LastCompleted!.Success);

        CreateFile(SourceDir, "Real.iso", 1024);
        await Service.CopyFileAsync("Real.iso");

        Assert.IsTrue(File.Exists(Path.Combine(TargetDir, "Real.iso")));
        Assert.IsTrue(Bridge.LastCompleted!.Success);
    }

    [TestMethod]
    public async Task CopyFile_TargetDirDisappearsAfterPreFlight()
    {
        CreateFile(SourceDir, "Game.iso", 1024);
        var tempTarget = Path.Combine(Path.GetTempPath(), "SyncTest_vanish_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempTarget);
        var svc = CreateService(syncPath: tempTarget);
        Directory.Delete(tempTarget, true);

        await svc.CopyFileAsync("Game.iso");

        Assert.IsNotNull(Bridge.LastCompleted);
        Assert.IsFalse(Bridge.LastCompleted.Success);
    }
}
