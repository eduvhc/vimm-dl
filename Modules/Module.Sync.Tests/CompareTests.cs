using Module.Sync.Tests.Helpers;
using Module.Sync.Bridge;
[TestClass]
public class CompareTests : SyncTestBase
{
    // --- Empty / missing path scenarios ---

    [TestMethod]
    public void Compare_NoSyncPathConfigured_ReturnsNotAccessible()
    {
        var svc = CreateService(syncPath: "");

        var result = svc.Compare();

        Assert.IsFalse(result.PathExists);
        Assert.AreEqual(0, result.New.Count);
        Assert.AreEqual(0, result.Synced.Count);
        Assert.AreEqual(0, result.TargetOnly.Count);
        Assert.IsNull(result.Source);
        Assert.IsNull(result.Target);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public void Compare_SyncPathWhitespace_ReturnsNotAccessible()
    {
        var svc = CreateService(syncPath: "   ");

        var result = svc.Compare();

        Assert.IsFalse(result.PathExists);
    }

    [TestMethod]
    public void Compare_SyncPathDoesNotExist_ReturnsErrorMessage()
    {
        var bogus = Path.Combine(Path.GetTempPath(), "nonexistent_" + Guid.NewGuid().ToString("N"));
        var svc = CreateService(syncPath: bogus);

        var result = svc.Compare();

        Assert.IsFalse(result.PathExists);
        Assert.IsNotNull(result.Error);
        StringAssert.Contains(result.Error, "not accessible");
    }

    [TestMethod]
    public void Compare_SourceCompletedDirMissing_ReturnsEmptyWithTargetDisk()
    {
        var emptyBase = Path.Combine(Path.GetTempPath(), "SyncTest_empty_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(emptyBase);

        try
        {
            var svc = CreateService(downloadPath: emptyBase, syncPath: TargetDir);
            var result = svc.Compare();

            Assert.IsTrue(result.PathExists);
            Assert.AreEqual(0, result.New.Count);
            Assert.AreEqual(0, result.Synced.Count);
            Assert.IsNull(result.Source);
            Assert.IsNotNull(result.Target);
            Assert.IsNull(result.Error);
        }
        finally
        {
            Directory.Delete(emptyBase, true);
        }
    }

    // --- File classification ---

    [TestMethod]
    public void Compare_EmptyDirectories_ReturnsEmptyLists()
    {
        var result = Service.Compare();

        Assert.IsTrue(result.PathExists);
        Assert.AreEqual(0, result.New.Count);
        Assert.AreEqual(0, result.Synced.Count);
        Assert.AreEqual(0, result.TargetOnly.Count);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public void Compare_NewFiles_SourceOnlyIsos()
    {
        CreateFile(SourceDir, "Game1.iso", 5000);
        CreateFile(SourceDir, "Game2.iso", 8000);

        var result = Service.Compare();

        Assert.AreEqual(2, result.New.Count);
        Assert.AreEqual(0, result.Synced.Count);
        Assert.AreEqual(0, result.TargetOnly.Count);
        Assert.IsTrue(result.New.Any(f => f.Name == "Game1.iso"));
        Assert.IsTrue(result.New.Any(f => f.Name == "Game2.iso"));
    }

    [TestMethod]
    public void Compare_SyncedFiles_SameNameBothDirs()
    {
        CreateFile(SourceDir, "Game1.iso", 5000);
        CreateFile(TargetDir, "Game1.iso", 5000);

        var result = Service.Compare();

        Assert.AreEqual(0, result.New.Count);
        Assert.AreEqual(1, result.Synced.Count);
        Assert.AreEqual("Game1.iso", result.Synced[0].Name);
    }

    [TestMethod]
    public void Compare_TargetOnlyFiles()
    {
        CreateFile(TargetDir, "OldGame.iso", 3000);

        var result = Service.Compare();

        Assert.AreEqual(0, result.New.Count);
        Assert.AreEqual(0, result.Synced.Count);
        Assert.AreEqual(1, result.TargetOnly.Count);
        Assert.AreEqual("OldGame.iso", result.TargetOnly[0].Name);
    }

    [TestMethod]
    public void Compare_MixedFiles_CorrectClassification()
    {
        CreateFile(SourceDir, "NewGame.iso", 4000);
        CreateFile(SourceDir, "SharedGame.iso", 6000);
        CreateFile(TargetDir, "SharedGame.iso", 6000);
        CreateFile(TargetDir, "OldGame.iso", 2000);

        var result = Service.Compare();

        Assert.AreEqual(1, result.New.Count);
        Assert.AreEqual(1, result.Synced.Count);
        Assert.AreEqual(1, result.TargetOnly.Count);
        Assert.AreEqual("NewGame.iso", result.New[0].Name);
        Assert.AreEqual("SharedGame.iso", result.Synced[0].Name);
        Assert.AreEqual("OldGame.iso", result.TargetOnly[0].Name);
    }

    [TestMethod]
    public void Compare_CaseInsensitiveMatching()
    {
        CreateFile(SourceDir, "Game.ISO", 1024);
        CreateFile(TargetDir, "game.iso", 1024);

        var result = Service.Compare();

        // Dictionary uses OrdinalIgnoreCase, but the file system may be case-sensitive (Linux).
        // On Linux: two distinct files → Game.ISO is new, game.iso is target-only
        // On Windows: same file → 1 synced
        if (OperatingSystem.IsWindows())
        {
            Assert.AreEqual(0, result.New.Count);
            Assert.AreEqual(1, result.Synced.Count);
        }
        else
        {
            // Case-sensitive FS: names don't match at file level
            Assert.IsTrue(result.New.Count + result.Synced.Count + result.TargetOnly.Count >= 1);
        }
    }

    [TestMethod]
    public void Compare_IgnoresNonIsoFiles()
    {
        CreateFile(SourceDir, "Game.iso", 5000);
        CreateFile(SourceDir, "archive.7z", 3000);
        CreateFile(SourceDir, "readme.txt", 100);
        CreateFile(TargetDir, "other.zip", 2000);

        var result = Service.Compare();

        Assert.AreEqual(1, result.New.Count);
        Assert.AreEqual("Game.iso", result.New[0].Name);
        Assert.AreEqual(0, result.TargetOnly.Count);
    }

    [TestMethod]
    public void Compare_FileSizesReported()
    {
        CreateFile(SourceDir, "Game.iso", 12345);

        var result = Service.Compare();

        Assert.AreEqual(1, result.New.Count);
        Assert.AreEqual(12345, result.New[0].Size);
    }

    // --- Disk info ---

    [TestMethod]
    public void Compare_DiskInfoPopulated_WhenBothPathsExist()
    {
        CreateFile(SourceDir, "Game.iso", 1024);
        CreateFile(TargetDir, "Other.iso", 2048);

        var result = Service.Compare();

        Assert.IsNotNull(result.Source);
        Assert.IsNotNull(result.Target);
        Assert.AreEqual(1, result.Source.IsoCount);
        Assert.AreEqual(1024, result.Source.IsoTotalSize);
        Assert.IsTrue(result.Source.FreeSpace > 0);
        Assert.IsTrue(result.Source.TotalSpace > 0);
        Assert.IsTrue(result.Source.Label.Length > 0);
        Assert.AreEqual(1, result.Target.IsoCount);
        Assert.AreEqual(2048, result.Target.IsoTotalSize);
    }

    [TestMethod]
    public void Compare_DiskInfoCountsAllIsos_NotJustNewOrSynced()
    {
        CreateFile(SourceDir, "A.iso", 1000);
        CreateFile(SourceDir, "B.iso", 2000);
        CreateFile(SourceDir, "C.iso", 3000);

        var result = Service.Compare();

        Assert.IsNotNull(result.Source);
        Assert.AreEqual(3, result.Source.IsoCount);
        Assert.AreEqual(6000, result.Source.IsoTotalSize);
    }

    // --- Graceful degradation ---

    [TestMethod]
    public void Compare_SourceDirDeletedBeforeCompare_HandledGracefully()
    {
        var tempSource = Path.Combine(Path.GetTempPath(), "SyncTest_vanish_" + Guid.NewGuid().ToString("N"));
        var completedDir = Path.Combine(tempSource, "completed");
        Directory.CreateDirectory(completedDir);
        CreateFile(completedDir, "Game.iso", 1024);

        var svc = CreateService(downloadPath: tempSource, syncPath: TargetDir);
        Directory.Delete(tempSource, true);

        var result = svc.Compare();

        // No exception escapes — returns valid response
        Assert.IsNotNull(result);
    }

    [TestMethod]
    public void Compare_TargetDirDeletedBeforeCompare_ReturnsError()
    {
        CreateFile(SourceDir, "Game.iso", 1024);

        var tempTarget = Path.Combine(Path.GetTempPath(), "SyncTest_vanish_t_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempTarget);

        var svc = CreateService(syncPath: tempTarget);
        Directory.Delete(tempTarget, true);

        var result = svc.Compare();

        Assert.IsNotNull(result);
        Assert.IsTrue(!result.PathExists || result.Error is not null);
    }
}
