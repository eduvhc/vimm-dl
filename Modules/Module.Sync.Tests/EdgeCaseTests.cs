using Module.Sync.Tests.Helpers;
using Module.Sync.Bridge;
using Module.Sync;

[TestClass]
public class EdgeCaseTests : SyncTestBase
{
    // --- Path accessibility ---

    [TestMethod]
    public void IsPathAccessible_ExistingDir_ReturnsTrue()
    {
        Assert.IsTrue(SyncService.IsPathAccessible(TargetDir));
    }

    [TestMethod]
    public void IsPathAccessible_NonExistentDir_ReturnsFalse()
    {
        var bogus = Path.Combine(Path.GetTempPath(), "does_not_exist_" + Guid.NewGuid().ToString("N"));
        Assert.IsFalse(SyncService.IsPathAccessible(bogus));
    }

    [TestMethod]
    public void IsPathAccessible_EmptyString_ReturnsFalse()
    {
        Assert.IsFalse(SyncService.IsPathAccessible(""));
    }

    // --- Special filenames ---

    [TestMethod]
    public void Compare_FilenamesWithSpecialChars()
    {
        CreateFile(SourceDir, "God of War\u00ae III - BCES-00510.iso", 2048);
        CreateFile(TargetDir, "God of War\u00ae III - BCES-00510.iso", 2048);

        var result = Service.Compare();

        Assert.AreEqual(0, result.New.Count);
        Assert.AreEqual(1, result.Synced.Count);
    }

    [TestMethod]
    public void Compare_FilenamesWithBrackets()
    {
        CreateFile(SourceDir, "Call of Duty Black Ops II [BLUS31141].iso", 3000);

        var result = Service.Compare();

        Assert.AreEqual(1, result.New.Count);
    }

    [TestMethod]
    public void Compare_FilenamesWithSpaces()
    {
        CreateFile(SourceDir, "Saints Row IV - The Full Package - BLES-02019.iso", 1500);
        CreateFile(TargetDir, "Saints Row IV - The Full Package - BLES-02019.iso", 1500);

        var result = Service.Compare();

        Assert.AreEqual(1, result.Synced.Count);
    }

    // --- Zero-byte files ---

    [TestMethod]
    public void Compare_ZeroByteIso_StillDetected()
    {
        CreateFile(SourceDir, "Empty.iso", 0);

        var result = Service.Compare();

        Assert.AreEqual(1, result.New.Count);
        Assert.AreEqual(0, result.New[0].Size);
    }

    [TestMethod]
    public async Task CopyFile_ZeroByteFile_CopiesSuccessfully()
    {
        CreateFile(SourceDir, "Empty.iso", 0);

        await Service.CopyFileAsync("Empty.iso");

        Assert.IsTrue(File.Exists(Path.Combine(TargetDir, "Empty.iso")));
        Assert.IsTrue(Bridge.LastCompleted!.Success);
    }

    // --- Large file ---

    [TestMethod]
    public async Task CopyFile_LargeFile_ContentIntegrity()
    {
        var size = 1024 * 1024 * 10; // 10 MB
        var content = new byte[size];
        Random.Shared.NextBytes(content);
        File.WriteAllBytes(Path.Combine(SourceDir, "Large.iso"), content);

        await Service.CopyFileAsync("Large.iso");

        var destContent = File.ReadAllBytes(Path.Combine(TargetDir, "Large.iso"));
        Assert.AreEqual(content.Length, destContent.Length);
        Assert.AreEqual(content[0], destContent[0]);
        Assert.AreEqual(content[size / 2], destContent[size / 2]);
        Assert.AreEqual(content[^1], destContent[^1]);
    }

    // --- GetDiskInfo ---

    [TestMethod]
    public void GetDiskInfo_ValidPath_ReturnsInfo()
    {
        CreateFile(TargetDir, "A.iso", 1000);
        CreateFile(TargetDir, "B.iso", 2000);

        var info = SyncService.GetDiskInfo(TargetDir);

        Assert.IsNotNull(info);
        Assert.AreEqual(2, info.IsoCount);
        Assert.AreEqual(3000, info.IsoTotalSize);
        Assert.IsTrue(info.FreeSpace > 0);
        Assert.IsTrue(info.TotalSpace > 0);
        Assert.IsTrue(info.FreeSpace <= info.TotalSpace);
    }

    [TestMethod]
    public void GetDiskInfo_EmptyDir_ZeroIsos()
    {
        var info = SyncService.GetDiskInfo(TargetDir);

        Assert.IsNotNull(info);
        Assert.AreEqual(0, info.IsoCount);
        Assert.AreEqual(0, info.IsoTotalSize);
    }

    [TestMethod]
    public void GetDiskInfo_IgnoresNonIsoFiles()
    {
        CreateFile(TargetDir, "game.iso", 5000);
        CreateFile(TargetDir, "archive.7z", 3000);
        CreateFile(TargetDir, "notes.txt", 100);

        var info = SyncService.GetDiskInfo(TargetDir);

        Assert.IsNotNull(info);
        Assert.AreEqual(1, info.IsoCount);
        Assert.AreEqual(5000, info.IsoTotalSize);
    }

    // --- FormatBytes ---

    [TestMethod]
    public void FormatBytes_GB()
    {
        Assert.AreEqual("1.00 GB", SyncService.FormatBytes(1073741824));
    }

    [TestMethod]
    public void FormatBytes_MB()
    {
        Assert.AreEqual("1.00 MB", SyncService.FormatBytes(1048576));
    }

    [TestMethod]
    public void FormatBytes_KB()
    {
        Assert.AreEqual("1.00 KB", SyncService.FormatBytes(1024));
    }

    // --- IsDiskError ---

    [TestMethod]
    public void IsDiskError_DeviceNotReady_True()
    {
        var ex = new IOException("Not ready", unchecked((int)0x80070015));
        Assert.IsTrue(SyncService.IsDiskError(ex));
    }

    [TestMethod]
    public void IsDiskError_DiskFull_True()
    {
        var ex = new IOException("Disk full", unchecked((int)0x80070070));
        Assert.IsTrue(SyncService.IsDiskError(ex));
    }

    [TestMethod]
    public void IsDiskError_RegularIOException_False()
    {
        var ex = new IOException("Some other error");
        Assert.IsFalse(SyncService.IsDiskError(ex));
    }

    // --- State safety ---

    [TestMethod]
    public async Task SequentialCopies_NoStateLeaks()
    {
        CreateFile(SourceDir, "A.iso", 1024);
        CreateFile(SourceDir, "B.iso", 2048);

        await Service.CopyFileAsync("A.iso");
        Assert.IsFalse(Service.IsCopying);
        Assert.IsNull(Service.CurrentFile);

        await Service.CopyFileAsync("B.iso");
        Assert.IsFalse(Service.IsCopying);
        Assert.IsNull(Service.CurrentFile);
    }

    [TestMethod]
    public async Task FailedCopy_StateResets()
    {
        await Service.CopyFileAsync("Ghost.iso");

        Assert.IsFalse(Service.IsCopying);
        Assert.IsNull(Service.CurrentFile);
        Assert.AreEqual(0.0, Service.CurrentProgress);
    }

    // --- GetCompletedDir ---

    [TestMethod]
    public void GetCompletedDir_UsesConfiguredPath()
    {
        var dir = Service.GetCompletedDir();
        Assert.IsTrue(dir.EndsWith(Path.Combine("completed")));
        StringAssert.Contains(dir, BaseDir);
    }

    [TestMethod]
    public void GetCompletedDir_FallsBackToUserDownloads_WhenEmpty()
    {
        var svc = CreateService(downloadPath: "", syncPath: TargetDir);
        var dir = svc.GetCompletedDir();

        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads", "completed");
        Assert.AreEqual(expected, dir);
    }

    // --- SetSyncPath ---

    [TestMethod]
    public void SetSyncPath_UpdatesPath()
    {
        Service.SetSyncPath("/new/path");
        Assert.AreEqual("/new/path", Service.GetSyncPath());
    }

    [TestMethod]
    public void Configure_SetsBothPaths()
    {
        var svc = CreateService(downloadPath: "/dl", syncPath: "/sync");
        Assert.AreEqual("/sync", svc.GetSyncPath());
        StringAssert.Contains(svc.GetCompletedDir(), "/dl");
    }
}
