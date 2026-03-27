using Module.Core.Testing;
using Module.Extractor.Tests.Helpers;

/// <summary>
/// Integration tests that run real 7z inside a Docker container.
/// Requires Docker to be running. Skips gracefully if unavailable.
/// </summary>
[TestClass]
public class ZipExtractContainerTests : ExtractorTestBase
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
        catch
        {
            _dockerAvailable = false;
        }
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

    private static string C(string hostPath) => ToolsContainer.ToContainerPath(hostPath);

    // --- 7z availability ---

    [TestMethod]
    public async Task SevenZip_IsAvailable()
    {
        RequireDocker();
        var (exitCode, stdout, _) = await _container!.ExecAsync(["7z"]);
        Assert.AreEqual(0, exitCode);
        StringAssert.Contains(stdout, "7-Zip");
    }

    // --- QuickCheck (7z l) ---

    [TestMethod]
    public async Task QuickCheck_InvalidFile_ReturnsFalse()
    {
        RequireDocker();
        var fakePath = Path.Combine(Tmp.Root, "fake.7z");
        File.WriteAllBytes(fakePath, [0x00, 0x01, 0x02, 0x03, 0xFF]);

        var (exitCode, _, _) = await _container!.ExecAsync(
            ["7z", "l", C(fakePath), "-y"]);

        Assert.AreNotEqual(0, exitCode);
    }

    [TestMethod]
    public async Task QuickCheck_EmptyFile_ReturnsFalse()
    {
        RequireDocker();
        var emptyPath = Path.Combine(Tmp.Root, "empty.7z");
        File.WriteAllBytes(emptyPath, []);

        var (exitCode, _, _) = await _container!.ExecAsync(
            ["7z", "l", C(emptyPath), "-y"]);

        Assert.AreNotEqual(0, exitCode);
    }

    [TestMethod]
    public async Task QuickCheck_ValidArchive_ReturnsTrue()
    {
        RequireDocker();
        var inputDir = Tmp.CreateSubDir("qc_input");
        TempDirectory.CreateFile(inputDir, "data.bin", 512);

        var archivePath = Path.Combine(Tmp.Root, "qc_valid.7z");
        await _container!.ExecAsync(["7z", "a", C(archivePath), $"{C(inputDir)}/*"]);

        var (exitCode, _, _) = await _container!.ExecAsync(
            ["7z", "l", C(archivePath), "-y"]);

        Assert.AreEqual(0, exitCode);
    }

    // --- Extract (7z x) ---

    [TestMethod]
    public async Task Extract_InvalidArchive_ReturnsFalse()
    {
        RequireDocker();
        var fakePath = Path.Combine(Tmp.Root, "bad.7z");
        File.WriteAllBytes(fakePath, [0xDE, 0xAD, 0xBE, 0xEF]);
        var outDir = Tmp.CreateSubDir("bad_out");

        var (exitCode, _, _) = await _container!.ExecAsync(
            ["7z", "x", C(fakePath), $"-o{C(outDir)}", "-y"]);

        Assert.AreNotEqual(0, exitCode);
    }

    [TestMethod]
    public async Task Extract_CreatesOutputDir()
    {
        RequireDocker();
        var inputDir = Tmp.CreateSubDir("dir_input");
        TempDirectory.CreateFile(inputDir, "file.bin", 256);

        var archivePath = Path.Combine(Tmp.Root, "dir_test.7z");
        await _container!.ExecAsync(["7z", "a", C(archivePath), $"{C(inputDir)}/*"]);

        var outDir = Path.Combine(Tmp.Root, "new_output_dir");
        Assert.IsFalse(Directory.Exists(outDir));

        await _container!.ExecAsync(
            ["7z", "x", C(archivePath), $"-o{C(outDir)}", "-y"]);

        Assert.IsTrue(Directory.Exists(outDir));
        Assert.IsTrue(File.Exists(Path.Combine(outDir, "file.bin")));
    }

    // --- Content integrity ---

    [TestMethod]
    public async Task Extract_RoundTrip_ContentMatches()
    {
        RequireDocker();
        var inputDir = Tmp.CreateSubDir("rt_input");
        var content = new byte[2048];
        Random.Shared.NextBytes(content);
        TempDirectory.CreateFile(inputDir, "payload.bin", content);

        var archivePath = Path.Combine(Tmp.Root, "roundtrip.7z");
        var (exitCode1, _, stderr1) = await _container!.ExecAsync(
            ["7z", "a", C(archivePath), $"{C(inputDir)}/*"]);
        Assert.AreEqual(0, exitCode1, $"Create failed: {stderr1}");

        var outputDir = Tmp.CreateSubDir("rt_output");
        var (exitCode2, _, stderr2) = await _container!.ExecAsync(
            ["7z", "x", C(archivePath), $"-o{C(outputDir)}", "-y"]);
        Assert.AreEqual(0, exitCode2, $"Extract failed: {stderr2}");

        var extracted = File.ReadAllBytes(Path.Combine(outputDir, "payload.bin"));
        CollectionAssert.AreEqual(content, extracted);
    }

    [TestMethod]
    public async Task Extract_MultipleFiles_AllPresent()
    {
        RequireDocker();
        var inputDir = Tmp.CreateSubDir("multi");
        TempDirectory.CreateFile(inputDir, "a.bin", 1024);
        TempDirectory.CreateFile(inputDir, "b.txt", 512);
        TempDirectory.CreateFile(inputDir, "c.dat", 256);

        var archivePath = Path.Combine(Tmp.Root, "multi.7z");
        await _container!.ExecAsync(
            ["7z", "a", C(archivePath), $"{C(inputDir)}/*"]);

        var outDir = Tmp.CreateSubDir("multi_out");
        await _container!.ExecAsync(
            ["7z", "x", C(archivePath), $"-o{C(outDir)}", "-y"]);

        Assert.IsTrue(File.Exists(Path.Combine(outDir, "a.bin")));
        Assert.IsTrue(File.Exists(Path.Combine(outDir, "b.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(outDir, "c.dat")));
    }

    [TestMethod]
    public async Task Extract_EmptyArchive_NoFiles()
    {
        RequireDocker();
        var emptyDir = Tmp.CreateSubDir("empty_src");

        var archivePath = Path.Combine(Tmp.Root, "empty.7z");
        await _container!.ExecAsync(
            ["7z", "a", C(archivePath), $"{C(emptyDir)}/*"]);

        var outDir = Tmp.CreateSubDir("empty_out");
        await _container!.ExecAsync(
            ["7z", "x", C(archivePath), $"-o{C(outDir)}", "-y"]);

        Assert.AreEqual(0, Directory.GetFiles(outDir).Length);
    }
}
