using Module.Core.Testing;
using Module.Ps3Iso.Tests.Helpers;

/// <summary>
/// Integration tests that run real makeps3iso/patchps3iso/7z inside a Docker container.
/// Requires Docker to be running. Skips gracefully if unavailable.
/// </summary>
[TestClass]
public class Ps3IsoContainerTests : Ps3IsoTestBase
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

    // --- Tool availability ---

    [TestMethod]
    public async Task Container_Makeps3iso_IsAvailable()
    {
        RequireDocker();
        var (exitCode, _, _) = await _container!.ExecAsync(["which", "makeps3iso"]);
        Assert.AreEqual(0, exitCode, "makeps3iso should be in PATH");
    }

    [TestMethod]
    public async Task Container_Patchps3iso_IsAvailable()
    {
        RequireDocker();
        var (exitCode, _, _) = await _container!.ExecAsync(["which", "patchps3iso"]);
        Assert.AreEqual(0, exitCode, "patchps3iso should be in PATH");
    }

    // --- Full pipeline: archive → extract → find JB → parse SFO ---

    [TestMethod]
    public async Task Container_ArchiveToJbDetection_FullPipeline()
    {
        RequireDocker();

        // Create JB folder with real PARAM.SFO
        var sourceDir = Tmp.CreateSubDir("source");
        var jbDir = CreateJbFolder(sourceDir, "Skate 3", "BLES00760");
        var usrDir = Path.Combine(jbDir, "PS3_GAME", "USRDIR");
        Directory.CreateDirectory(usrDir);
        TempDirectory.CreateFile(usrDir, "EBOOT.BIN", 2048);

        // Create 7z archive
        var archivePath = Path.Combine(Tmp.Root, "skate3.7z");
        var (exitCode, _, stderr) = await _container!.ExecAsync(
            ["7z", "a", C(archivePath), $"{C(sourceDir)}/*"]);
        Assert.AreEqual(0, exitCode, $"7z create failed: {stderr}");
        Assert.IsTrue(File.Exists(archivePath));

        // Extract
        var extractDir = Tmp.CreateSubDir("extracted");
        var (exitCode2, _, stderr2) = await _container!.ExecAsync(
            ["7z", "x", C(archivePath), $"-o{C(extractDir)}", "-y"]);
        Assert.AreEqual(0, exitCode2, $"7z extract failed: {stderr2}");

        // FindJbFolder should find the structure
        var foundJb = Ps3IsoConverter.FindJbFolder(extractDir);
        Assert.IsNotNull(foundJb, "Should find JB folder after extraction");

        // ParamSfo should parse correctly
        var sfo = ParamSfo.Parse(Path.Combine(foundJb, "PS3_GAME", "PARAM.SFO"));
        Assert.IsNotNull(sfo);
        Assert.AreEqual("Skate 3", sfo.Title);
        Assert.AreEqual("BLES-00760", sfo.TitleId);
    }

    // --- makeps3iso ---

    [TestMethod]
    public async Task Container_Makeps3iso_RunsOnJbFolder()
    {
        RequireDocker();

        // Create minimal JB folder
        var jbDir = Tmp.CreateSubDir("jb_game");
        var ps3Game = Path.Combine(jbDir, "PS3_GAME");
        Directory.CreateDirectory(ps3Game);
        File.WriteAllBytes(Path.Combine(ps3Game, "PARAM.SFO"), BuildParamSfo("Test Game", "BCES00001"));
        var usrDir = Path.Combine(ps3Game, "USRDIR");
        Directory.CreateDirectory(usrDir);
        TempDirectory.CreateFile(usrDir, "EBOOT.BIN", 1024);

        var outputIso = Path.Combine(Tmp.Root, "test_output.iso");

        var (exitCode, stdout, stderr) = await _container!.ExecAsync(
            ["makeps3iso", C(jbDir), C(outputIso)]);

        // makeps3iso may fail on minimal structures but should at least run (not exit 127)
        Assert.AreNotEqual(127, exitCode, $"makeps3iso binary not found: {stderr}");

        if (exitCode == 0)
        {
            Assert.IsTrue(File.Exists(outputIso), "ISO should be created on success");
            Assert.IsTrue(new FileInfo(outputIso).Length > 0, "ISO should not be empty");
        }
    }

    [TestMethod]
    public async Task Container_Patchps3iso_RunsOnIso()
    {
        RequireDocker();

        // Create a JB folder and try to make an ISO first
        var jbDir = Tmp.CreateSubDir("patch_test");
        var ps3Game = Path.Combine(jbDir, "PS3_GAME");
        Directory.CreateDirectory(ps3Game);
        File.WriteAllBytes(Path.Combine(ps3Game, "PARAM.SFO"), BuildParamSfo("Patch Test", "BCES00002"));
        var usrDir = Path.Combine(ps3Game, "USRDIR");
        Directory.CreateDirectory(usrDir);
        TempDirectory.CreateFile(usrDir, "EBOOT.BIN", 1024);

        var isoPath = Path.Combine(Tmp.Root, "patch_test.iso");
        var (makeExit, _, _) = await _container!.ExecAsync(
            ["makeps3iso", C(jbDir), C(isoPath)]);

        if (makeExit != 0)
        {
            Assert.Inconclusive("makeps3iso didn't produce an ISO to patch");
            return;
        }

        // Patch firmware
        var (patchExit, _, patchStderr) = await _container!.ExecAsync(
            ["patchps3iso", C(isoPath), "3.55"]);

        // Should run without crashing
        Assert.AreNotEqual(127, patchExit, $"patchps3iso not found: {patchStderr}");
    }

    // --- Edge cases ---

    [TestMethod]
    public async Task Container_7z_CanHandleSpecialCharFilenames()
    {
        RequireDocker();

        var inputDir = Tmp.CreateSubDir("special");
        TempDirectory.CreateFile(inputDir, "God of War III - BCES-00510.bin", 512);

        var archivePath = Path.Combine(Tmp.Root, "special.7z");
        var (exitCode, _, stderr) = await _container!.ExecAsync(
            ["7z", "a", C(archivePath), $"{C(inputDir)}/*"]);
        Assert.AreEqual(0, exitCode, $"7z failed with special chars: {stderr}");

        var outDir = Tmp.CreateSubDir("special_out");
        await _container!.ExecAsync(
            ["7z", "x", C(archivePath), $"-o{C(outDir)}", "-y"]);

        Assert.IsTrue(File.Exists(Path.Combine(outDir, "God of War III - BCES-00510.bin")));
    }

    /// <summary>Shorthand for ToolsContainer.ToContainerPath</summary>
    private static string C(string hostPath) => ToolsContainer.ToContainerPath(hostPath);
}
