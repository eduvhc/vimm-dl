using Module.Core.Testing;
using Module.Extractor.Tests.Helpers;

[TestClass]
public class ZipExtractEdgeCaseTests : ExtractorTestBase
{
    private static ToolsContainer? _container;
    private static bool _dockerAvailable;

    [ClassInitialize]
    public static async Task ClassInit(TestContext _)
    {
        try
        {
            _container = new ToolsContainer();
            await _container.StartAsync();
            _dockerAvailable = true;
        }
        catch { _dockerAvailable = false; }
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        if (_container != null) await _container.DisposeAsync();
    }

    private void RequireDocker()
    {
        if (!_dockerAvailable) Assert.Inconclusive("Docker not available");
    }

    private static string C(string p) => ToolsContainer.ToContainerPath(p);

    // --- Corrupt / malformed archives ---

    [TestMethod]
    public async Task Extract_TruncatedArchive_Fails()
    {
        RequireDocker();

        // Create valid archive, then truncate it
        var inputDir = Tmp.CreateSubDir("trunc_in");
        TempDirectory.CreateFile(inputDir, "data.bin", 4096);

        var archivePath = Path.Combine(Tmp.Root, "truncated.7z");
        await _container!.ExecAsync(["7z", "a", C(archivePath), $"{C(inputDir)}/*"]);

        // Truncate to half
        var bytes = File.ReadAllBytes(archivePath);
        File.WriteAllBytes(archivePath, bytes[..(bytes.Length / 2)]);

        var outDir = Tmp.CreateSubDir("trunc_out");
        var (exitCode, _, _) = await _container!.ExecAsync(
            ["7z", "x", C(archivePath), $"-o{C(outDir)}", "-y"]);

        Assert.AreNotEqual(0, exitCode);
    }

    [TestMethod]
    public async Task QuickCheck_TruncatedArchive_Fails()
    {
        RequireDocker();

        var inputDir = Tmp.CreateSubDir("qc_trunc_in");
        TempDirectory.CreateFile(inputDir, "data.bin", 2048);

        var archivePath = Path.Combine(Tmp.Root, "qc_trunc.7z");
        await _container!.ExecAsync(["7z", "a", C(archivePath), $"{C(inputDir)}/*"]);

        var bytes = File.ReadAllBytes(archivePath);
        File.WriteAllBytes(archivePath, bytes[..(bytes.Length / 3)]);

        var (exitCode, _, _) = await _container!.ExecAsync(
            ["7z", "l", C(archivePath), "-y"]);

        Assert.AreNotEqual(0, exitCode);
    }

    [TestMethod]
    public async Task Extract_ZeroByteArchive_Fails()
    {
        RequireDocker();
        var archivePath = Path.Combine(Tmp.Root, "zero.7z");
        File.WriteAllBytes(archivePath, []);
        var outDir = Tmp.CreateSubDir("zero_out");

        var (exitCode, _, _) = await _container!.ExecAsync(
            ["7z", "x", C(archivePath), $"-o{C(outDir)}", "-y"]);

        Assert.AreNotEqual(0, exitCode);
    }

    [TestMethod]
    public async Task Extract_RandomGarbage_Fails()
    {
        RequireDocker();
        var garbage = new byte[8192];
        Random.Shared.NextBytes(garbage);
        var archivePath = Path.Combine(Tmp.Root, "garbage.7z");
        File.WriteAllBytes(archivePath, garbage);
        var outDir = Tmp.CreateSubDir("garbage_out");

        var (exitCode, _, _) = await _container!.ExecAsync(
            ["7z", "x", C(archivePath), $"-o{C(outDir)}", "-y"]);

        Assert.AreNotEqual(0, exitCode);
    }

    // --- Special filenames ---

    [TestMethod]
    public async Task Extract_FilenameWithSpaces()
    {
        RequireDocker();
        var inputDir = Tmp.CreateSubDir("spaces_in");
        TempDirectory.CreateFile(inputDir, "my game data.bin", 512);

        var archivePath = Path.Combine(Tmp.Root, "spaces.7z");
        await _container!.ExecAsync(["7z", "a", C(archivePath), $"{C(inputDir)}/*"]);

        var outDir = Tmp.CreateSubDir("spaces_out");
        await _container!.ExecAsync(["7z", "x", C(archivePath), $"-o{C(outDir)}", "-y"]);

        Assert.IsTrue(File.Exists(Path.Combine(outDir, "my game data.bin")));
    }

    [TestMethod]
    public async Task Extract_FilenameWithUnicode()
    {
        RequireDocker();
        var inputDir = Tmp.CreateSubDir("unicode_in");
        TempDirectory.CreateFile(inputDir, "God of War\u00ae.bin", 256);

        var archivePath = Path.Combine(Tmp.Root, "unicode.7z");
        await _container!.ExecAsync(["7z", "a", C(archivePath), $"{C(inputDir)}/*"]);

        var outDir = Tmp.CreateSubDir("unicode_out");
        await _container!.ExecAsync(["7z", "x", C(archivePath), $"-o{C(outDir)}", "-y"]);

        Assert.IsTrue(File.Exists(Path.Combine(outDir, "God of War\u00ae.bin")));
    }

    [TestMethod]
    public async Task Extract_FilenameWithDashes()
    {
        RequireDocker();
        var inputDir = Tmp.CreateSubDir("dash_in");
        TempDirectory.CreateFile(inputDir, "BCES-00510 - Game.bin", 256);

        var archivePath = Path.Combine(Tmp.Root, "dash.7z");
        await _container!.ExecAsync(["7z", "a", C(archivePath), $"{C(inputDir)}/*"]);

        var outDir = Tmp.CreateSubDir("dash_out");
        await _container!.ExecAsync(["7z", "x", C(archivePath), $"-o{C(outDir)}", "-y"]);

        Assert.IsTrue(File.Exists(Path.Combine(outDir, "BCES-00510 - Game.bin")));
    }

    // --- Archive content types ---

    [TestMethod]
    public async Task Extract_NestedDirectories_PreservesStructure()
    {
        RequireDocker();
        var inputDir = Tmp.CreateSubDir("nested_in");
        var sub1 = Path.Combine(inputDir, "level1", "level2");
        Directory.CreateDirectory(sub1);
        TempDirectory.CreateFile(sub1, "deep.bin", 128);
        TempDirectory.CreateFile(inputDir, "root.bin", 128);

        var archivePath = Path.Combine(Tmp.Root, "nested.7z");
        await _container!.ExecAsync(["7z", "a", C(archivePath), $"{C(inputDir)}/*", "-r"]);

        var outDir = Tmp.CreateSubDir("nested_out");
        await _container!.ExecAsync(["7z", "x", C(archivePath), $"-o{C(outDir)}", "-y"]);

        Assert.IsTrue(File.Exists(Path.Combine(outDir, "root.bin")));
        Assert.IsTrue(File.Exists(Path.Combine(outDir, "level1", "level2", "deep.bin")));
    }

    [TestMethod]
    public async Task Extract_LargeFile_ContentIntegrity()
    {
        RequireDocker();
        var inputDir = Tmp.CreateSubDir("large_in");
        var content = new byte[1024 * 1024]; // 1MB
        Random.Shared.NextBytes(content);
        TempDirectory.CreateFile(inputDir, "large.bin", content);

        var archivePath = Path.Combine(Tmp.Root, "large.7z");
        await _container!.ExecAsync(["7z", "a", C(archivePath), $"{C(inputDir)}/*"]);

        var outDir = Tmp.CreateSubDir("large_out");
        await _container!.ExecAsync(["7z", "x", C(archivePath), $"-o{C(outDir)}", "-y"]);

        var extracted = File.ReadAllBytes(Path.Combine(outDir, "large.bin"));
        CollectionAssert.AreEqual(content, extracted);
    }

    [TestMethod]
    public async Task Extract_OverwriteExistingFiles()
    {
        RequireDocker();
        var inputDir = Tmp.CreateSubDir("overwrite_in");
        var content = new byte[256];
        Random.Shared.NextBytes(content);
        TempDirectory.CreateFile(inputDir, "file.bin", content);

        var archivePath = Path.Combine(Tmp.Root, "overwrite.7z");
        await _container!.ExecAsync(["7z", "a", C(archivePath), $"{C(inputDir)}/*"]);

        // Pre-existing different file in output
        var outDir = Tmp.CreateSubDir("overwrite_out");
        File.WriteAllBytes(Path.Combine(outDir, "file.bin"), new byte[64]);

        await _container!.ExecAsync(["7z", "x", C(archivePath), $"-o{C(outDir)}", "-y"]);

        var extracted = File.ReadAllBytes(Path.Combine(outDir, "file.bin"));
        CollectionAssert.AreEqual(content, extracted);
    }

    // --- Pre-flight edge cases (no Docker needed) ---

    [TestMethod]
    public async Task QuickCheck_NullPath_ThrowsOrReturnsFalse()
    {
        // Passing empty string should return file not found
        var (valid, error) = await ZipExtract.QuickCheckAsync("");
        Assert.IsFalse(valid);
        Assert.IsNotNull(error);
    }

    [TestMethod]
    public async Task Extract_NullPath_ReturnsFalse()
    {
        var outDir = Tmp.CreateSubDir("null_out");
        var (success, error) = await ZipExtract.ExtractAsync("", outDir);
        Assert.IsFalse(success);
        Assert.IsNotNull(error);
    }

    [TestMethod]
    public async Task QuickCheck_DirectoryInsteadOfFile_ReturnsFalse()
    {
        var dir = Tmp.CreateSubDir("not_a_file");
        var (valid, error) = await ZipExtract.QuickCheckAsync(dir);
        Assert.IsFalse(valid);
    }

    [TestMethod]
    public async Task Extract_DirectoryInsteadOfFile_ReturnsFalse()
    {
        var dir = Tmp.CreateSubDir("not_a_file2");
        var outDir = Tmp.CreateSubDir("dir_extract_out");
        var (success, error) = await ZipExtract.ExtractAsync(dir, outDir);
        Assert.IsFalse(success);
    }
}
