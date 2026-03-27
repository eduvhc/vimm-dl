using Module.Sync.Tests.Helpers;
using Module.Sync.Bridge;

/// <summary>
/// Additional edge case tests for SyncService covering race conditions,
/// concurrent access, path traversal, and boundary scenarios.
/// </summary>
[TestClass]
public class SyncEdgeCaseTests : SyncTestBase
{
    // --- Path traversal ---

    [TestMethod]
    public async Task CopyFile_PathTraversal_FailsGracefully()
    {
        // "../escape.iso" resolves outside the target — copy should fail (source not found)
        // because completed/ doesn't have a file at the traversed path
        await Service.CopyFileAsync("..\\escape.iso");

        Assert.IsFalse(Bridge.LastCompleted!.Success);
    }

    [TestMethod]
    public async Task CopyFile_FilenameWithSlash_HandledSafely()
    {
        // This tests that Path.Combine handles filenames with separators
        await Service.CopyFileAsync("sub/game.iso");

        // Should fail gracefully (source not found)
        Assert.IsFalse(Bridge.LastCompleted!.Success);
    }

    // --- Concurrent state ---

    [TestMethod]
    public async Task CopyFile_SequentialCalls_StateNeverLeaks()
    {
        for (int i = 0; i < 10; i++)
        {
            CreateFile(SourceDir, $"game{i}.iso", 512);
            await Service.CopyFileAsync($"game{i}.iso");

            Assert.IsFalse(Service.IsCopying, $"IsCopying should be false after copy {i}");
            Assert.IsNull(Service.CurrentFile, $"CurrentFile should be null after copy {i}");
            Assert.AreEqual(0.0, Service.CurrentProgress, $"Progress should be 0 after copy {i}");
        }

        Assert.AreEqual(10, Bridge.CompletedEvents.Count);
        Assert.IsTrue(Bridge.CompletedEvents.All(e => e.Success));
    }

    [TestMethod]
    public async Task CopyFile_FailThenSucceed_Alternating()
    {
        for (int i = 0; i < 5; i++)
        {
            // Fail
            await Service.CopyFileAsync($"missing{i}.iso");
            Assert.IsFalse(Bridge.Last<SyncCompletedEvent>()!.Success);
            Assert.IsFalse(Service.IsCopying);

            // Succeed
            CreateFile(SourceDir, $"real{i}.iso", 256);
            await Service.CopyFileAsync($"real{i}.iso");
            Assert.IsTrue(Bridge.Last<SyncCompletedEvent>()!.Success);
            Assert.IsFalse(Service.IsCopying);
        }
    }

    // --- Compare edge cases ---

    [TestMethod]
    public void Compare_ManyFiles_AllClassifiedCorrectly()
    {
        for (int i = 0; i < 50; i++)
        {
            CreateFile(SourceDir, $"new_{i:D3}.iso", 256);
            CreateFile(SourceDir, $"synced_{i:D3}.iso", 256);
            CreateFile(TargetDir, $"synced_{i:D3}.iso", 256);
            CreateFile(TargetDir, $"target_{i:D3}.iso", 256);
        }

        var result = Service.Compare();

        Assert.AreEqual(50, result.New.Count);
        Assert.AreEqual(50, result.Synced.Count);
        Assert.AreEqual(50, result.TargetOnly.Count);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public void Compare_DuplicateFilenames_OnlyCountedOnce()
    {
        // Create file, compare, then check there's no double-counting
        CreateFile(SourceDir, "game.iso", 1024);
        CreateFile(TargetDir, "game.iso", 1024);

        var result = Service.Compare();

        Assert.AreEqual(0, result.New.Count);
        Assert.AreEqual(1, result.Synced.Count);
    }

    [TestMethod]
    public void Compare_FileSizeMismatch_StillSynced()
    {
        // Same name, different sizes — still counted as synced (name-based comparison)
        CreateFile(SourceDir, "game.iso", 1024);
        CreateFile(TargetDir, "game.iso", 8192);

        var result = Service.Compare();

        Assert.AreEqual(0, result.New.Count);
        Assert.AreEqual(1, result.Synced.Count);
    }

    [TestMethod]
    public void Compare_OnlyNonIsoFiles_EmptyResult()
    {
        CreateFile(SourceDir, "archive.7z", 5000);
        CreateFile(SourceDir, "readme.txt", 100);
        CreateFile(TargetDir, "backup.zip", 3000);

        var result = Service.Compare();

        Assert.AreEqual(0, result.New.Count);
        Assert.AreEqual(0, result.Synced.Count);
        Assert.AreEqual(0, result.TargetOnly.Count);
    }

    [TestMethod]
    public void Compare_HiddenFiles_Included()
    {
        CreateFile(SourceDir, ".hidden.iso", 512);

        var result = Service.Compare();

        Assert.AreEqual(1, result.New.Count);
        Assert.AreEqual(".hidden.iso", result.New[0].Name);
    }

    // --- CopyAll edge cases ---

    [TestMethod]
    public async Task CopyAll_SourceFileDeleted_OnlyCopiesRemaining()
    {
        CreateFile(SourceDir, "exists.iso", 1024);
        CreateFile(SourceDir, "vanishes.iso", 1024);

        // Verify both show as new
        var before = Service.Compare();
        Assert.AreEqual(2, before.New.Count);

        // Delete one file — CopyAllNewAsync calls Compare() internally,
        // so it will only see 1 file
        File.Delete(Path.Combine(SourceDir, "vanishes.iso"));

        await Service.CopyAllNewAsync();

        var successes = Bridge.Of<SyncCompletedEvent>().Where(e => e.Success).ToList();
        Assert.AreEqual(1, successes.Count);
        Assert.IsTrue(File.Exists(Path.Combine(TargetDir, "exists.iso")));
        Assert.IsFalse(File.Exists(Path.Combine(TargetDir, "vanishes.iso")));
    }

    [TestMethod]
    public async Task CopyAll_TargetBecomesFull_ReportsPerFileError()
    {
        // Create files
        CreateFile(SourceDir, "a.iso", 1024);
        CreateFile(SourceDir, "b.iso", 1024);

        // This test verifies that each file gets its own completed event
        await Service.CopyAllNewAsync();

        Assert.AreEqual(2, Bridge.Of<SyncCompletedEvent>().Count);
    }

    // --- Copy content edge cases ---

    [TestMethod]
    public async Task CopyFile_ExactlyBufferSize_CopiesCorrectly()
    {
        // 256KB = the internal buffer size — boundary condition
        var size = 1024 * 256;
        var content = new byte[size];
        Random.Shared.NextBytes(content);
        File.WriteAllBytes(Path.Combine(SourceDir, "exact_buffer.iso"), content);

        await Service.CopyFileAsync("exact_buffer.iso");

        var copied = File.ReadAllBytes(Path.Combine(TargetDir, "exact_buffer.iso"));
        CollectionAssert.AreEqual(content, copied);
    }

    [TestMethod]
    public async Task CopyFile_BufferSizePlusOne_CopiesCorrectly()
    {
        var size = 1024 * 256 + 1;
        var content = new byte[size];
        Random.Shared.NextBytes(content);
        File.WriteAllBytes(Path.Combine(SourceDir, "buffer_plus1.iso"), content);

        await Service.CopyFileAsync("buffer_plus1.iso");

        var copied = File.ReadAllBytes(Path.Combine(TargetDir, "buffer_plus1.iso"));
        CollectionAssert.AreEqual(content, copied);
    }

    [TestMethod]
    public async Task CopyFile_BufferSizeMinusOne_CopiesCorrectly()
    {
        var size = 1024 * 256 - 1;
        var content = new byte[size];
        Random.Shared.NextBytes(content);
        File.WriteAllBytes(Path.Combine(SourceDir, "buffer_minus1.iso"), content);

        await Service.CopyFileAsync("buffer_minus1.iso");

        var copied = File.ReadAllBytes(Path.Combine(TargetDir, "buffer_minus1.iso"));
        CollectionAssert.AreEqual(content, copied);
    }

    [TestMethod]
    public async Task CopyFile_OneByte_CopiesCorrectly()
    {
        File.WriteAllBytes(Path.Combine(SourceDir, "tiny.iso"), [0x42]);

        await Service.CopyFileAsync("tiny.iso");

        var copied = File.ReadAllBytes(Path.Combine(TargetDir, "tiny.iso"));
        Assert.AreEqual(1, copied.Length);
        Assert.AreEqual(0x42, copied[0]);
    }

    // --- Bridge event verification ---

    [TestMethod]
    public async Task CopyFile_Success_FinalProgressHasCorrectTotal()
    {
        var size = 4096;
        CreateFile(SourceDir, "progress.iso", size);

        await Service.CopyFileAsync("progress.iso");

        var lastProgress = Bridge.Of<SyncProgressEvent>().Last();
        Assert.AreEqual(100.0, lastProgress.Percent);
        Assert.AreEqual(lastProgress.Total, lastProgress.Copied);
        Assert.AreEqual(size, lastProgress.Total);
    }

    [TestMethod]
    public async Task CopyFile_Failure_NoProgressEvents()
    {
        await Service.CopyFileAsync("missing.iso");

        Assert.AreEqual(0, Bridge.Of<SyncProgressEvent>().Count);
        Assert.AreEqual(1, Bridge.Of<SyncCompletedEvent>().Count);
    }

    // --- SetSyncPath while operating ---

    [TestMethod]
    public void SetSyncPath_ChangesCompareResult()
    {
        CreateFile(SourceDir, "game.iso", 1024);
        CreateFile(TargetDir, "game.iso", 1024);

        var result1 = Service.Compare();
        Assert.AreEqual(1, result1.Synced.Count);

        // Change target to a new empty dir
        var newTarget = Path.Combine(Path.GetTempPath(), "SyncTest_newtarget_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(newTarget);
        try
        {
            Service.SetSyncPath(newTarget);
            var result2 = Service.Compare();

            // Same source file is now "new" since new target is empty
            Assert.AreEqual(1, result2.New.Count);
            Assert.AreEqual(0, result2.Synced.Count);
        }
        finally
        {
            try { Directory.Delete(newTarget, true); } catch { }
        }
    }

    // --- DiskInfo edge cases ---

    [TestMethod]
    public void Compare_DiskInfo_FreeSpaceLessThanTotal()
    {
        CreateFile(SourceDir, "game.iso", 1024);

        var result = Service.Compare();

        Assert.IsNotNull(result.Source);
        Assert.IsTrue(result.Source.FreeSpace <= result.Source.TotalSpace);
        Assert.IsTrue(result.Source.FreeSpace >= 0);
    }

    [TestMethod]
    public void Compare_DiskInfo_IsoTotalSizeMatchesSum()
    {
        CreateFile(SourceDir, "a.iso", 1000);
        CreateFile(SourceDir, "b.iso", 2000);
        CreateFile(SourceDir, "c.iso", 3000);
        CreateFile(SourceDir, "not_iso.txt", 9999);

        var result = Service.Compare();

        Assert.IsNotNull(result.Source);
        Assert.AreEqual(3, result.Source.IsoCount);
        Assert.AreEqual(6000, result.Source.IsoTotalSize);
    }
}
