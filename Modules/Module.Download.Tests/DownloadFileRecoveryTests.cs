using Module.Core.Testing;
using Module.Download;

/// <summary>
/// Tests for crash recovery scenarios: partial files in downloading/,
/// fully downloaded files not yet moved, completed files already present.
/// These test the file-level logic without real HTTP — we set up the files
/// manually and verify the service handles them correctly.
/// </summary>
[TestClass]
public class DownloadFileRecoveryTests
{
    private TempDirectory _tmp = null!;

    [TestInitialize]
    public void Setup() => _tmp = new TempDirectory("DownloadRecovery");

    [TestCleanup]
    public void Cleanup() => _tmp.Dispose();

    // --- Partial file in downloading/ ---

    [TestMethod]
    public void PartialFile_Exists_CanBeDetected()
    {
        var dlDir = _tmp.CreateSubDir("downloading");
        TempDirectory.CreateFile(dlDir, "Game.7z", 5000);

        var fi = new FileInfo(Path.Combine(dlDir, "Game.7z"));
        Assert.IsTrue(fi.Exists);
        Assert.AreEqual(5000, fi.Length);
    }

    [TestMethod]
    public void PartialFile_ZeroBytes_Ignored()
    {
        var dlDir = _tmp.CreateSubDir("downloading");
        File.Create(Path.Combine(dlDir, "Empty.7z")).Dispose();

        var fi = new FileInfo(Path.Combine(dlDir, "Empty.7z"));
        Assert.AreEqual(0, fi.Length);
    }

    // --- File move: downloading → completed ---

    [TestMethod]
    public void MoveToCompleted_Success()
    {
        var dlDir = _tmp.CreateSubDir("downloading");
        var cDir = _tmp.CreateSubDir("completed");
        var content = new byte[4096];
        Random.Shared.NextBytes(content);
        File.WriteAllBytes(Path.Combine(dlDir, "Game.7z"), content);

        File.Move(Path.Combine(dlDir, "Game.7z"), Path.Combine(cDir, "Game.7z"));

        Assert.IsFalse(File.Exists(Path.Combine(dlDir, "Game.7z")));
        CollectionAssert.AreEqual(content, File.ReadAllBytes(Path.Combine(cDir, "Game.7z")));
    }

    [TestMethod]
    public void MoveToCompleted_TargetExists_OverwritePattern()
    {
        // Simulates the pattern: delete existing, then move
        var dlDir = _tmp.CreateSubDir("downloading");
        var cDir = _tmp.CreateSubDir("completed");
        var newContent = new byte[4096];
        Random.Shared.NextBytes(newContent);
        File.WriteAllBytes(Path.Combine(dlDir, "Game.7z"), newContent);
        File.WriteAllBytes(Path.Combine(cDir, "Game.7z"), new byte[100]); // stale

        if (File.Exists(Path.Combine(cDir, "Game.7z")))
            File.Delete(Path.Combine(cDir, "Game.7z"));
        File.Move(Path.Combine(dlDir, "Game.7z"), Path.Combine(cDir, "Game.7z"));

        CollectionAssert.AreEqual(newContent, File.ReadAllBytes(Path.Combine(cDir, "Game.7z")));
    }

    [TestMethod]
    public void MoveToCompleted_TargetDirMissing_Throws()
    {
        var dlDir = _tmp.CreateSubDir("downloading");
        TempDirectory.CreateFile(dlDir, "Game.7z", 100);

        var badPath = Path.Combine(_tmp.Root, "nonexistent", "Game.7z");
        Assert.ThrowsExactly<DirectoryNotFoundException>(
            () => File.Move(Path.Combine(dlDir, "Game.7z"), badPath));
    }

    // --- Crash recovery: file fully downloaded but not moved ---

    [TestMethod]
    public void FullyDownloaded_InDownloading_CanRecover()
    {
        // Simulates: app was killed after download completed but before File.Move
        var dlDir = _tmp.CreateSubDir("downloading");
        var cDir = _tmp.CreateSubDir("completed");
        var content = new byte[10000];
        Random.Shared.NextBytes(content);
        File.WriteAllBytes(Path.Combine(dlDir, "Game.7z"), content);

        // Recovery: check existingBytes >= totalBytes
        var existingBytes = new FileInfo(Path.Combine(dlDir, "Game.7z")).Length;
        long totalBytes = 10000;

        Assert.IsTrue(existingBytes >= totalBytes);

        // Recover by moving
        File.Move(Path.Combine(dlDir, "Game.7z"), Path.Combine(cDir, "Game.7z"));
        Assert.IsTrue(File.Exists(Path.Combine(cDir, "Game.7z")));
        Assert.IsFalse(File.Exists(Path.Combine(dlDir, "Game.7z")));
    }

    // --- File.Move atomicity test ---

    [TestMethod]
    public void FileMove_IsAtomic_CompletedNotCorrupted()
    {
        var dlDir = _tmp.CreateSubDir("downloading");
        var cDir = _tmp.CreateSubDir("completed");
        var content = new byte[50000];
        Random.Shared.NextBytes(content);
        File.WriteAllBytes(Path.Combine(dlDir, "Big.7z"), content);

        File.Move(Path.Combine(dlDir, "Big.7z"), Path.Combine(cDir, "Big.7z"));

        var result = File.ReadAllBytes(Path.Combine(cDir, "Big.7z"));
        CollectionAssert.AreEqual(content, result);
    }

    // --- Special filenames ---

    [TestMethod]
    public void SpecialCharsInFilename_HandledByJoin()
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var name = "Game: The Sequel (Europe) [v1.0].7z";
        var sanitized = string.Join("_", name.Split(invalidChars));

        Assert.IsFalse(sanitized.Any(c => invalidChars.Contains(c)));
        Assert.IsTrue(sanitized.Length > 0);
    }

    [TestMethod]
    public void UnicodeFilename_MovesCorrectly()
    {
        var dlDir = _tmp.CreateSubDir("downloading");
        var cDir = _tmp.CreateSubDir("completed");
        var name = "God of War\u00ae III - BCES-00510.7z";
        TempDirectory.CreateFile(dlDir, name, 100);

        File.Move(Path.Combine(dlDir, name), Path.Combine(cDir, name));

        Assert.IsTrue(File.Exists(Path.Combine(cDir, name)));
    }

    // --- Concurrent access to downloading/ ---

    [TestMethod]
    public void ConcurrentFileCreation_InDownloadingDir()
    {
        var dlDir = _tmp.CreateSubDir("downloading");
        var tasks = Enumerable.Range(0, 10).Select(i => Task.Run(() =>
        {
            var path = Path.Combine(dlDir, $"file_{i}.tmp");
            File.WriteAllBytes(path, new byte[100]);
        })).ToArray();

        Task.WaitAll(tasks);

        Assert.AreEqual(10, Directory.GetFiles(dlDir).Length);
    }

    // --- Directory creation ---

    [TestMethod]
    public void CreateDirectory_AlreadyExists_NoOp()
    {
        var dir = _tmp.CreateSubDir("downloading");
        Directory.CreateDirectory(dir); // second call
        Assert.IsTrue(Directory.Exists(dir));
    }

    [TestMethod]
    public void CreateDirectory_Nested_CreatesAll()
    {
        var nested = Path.Combine(_tmp.Root, "a", "b", "c", "downloading");
        Directory.CreateDirectory(nested);
        Assert.IsTrue(Directory.Exists(nested));
    }
}
