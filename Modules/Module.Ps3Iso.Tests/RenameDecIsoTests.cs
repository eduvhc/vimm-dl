using Module.Core.Testing;
using Module.Ps3Iso.Tests.Helpers;
using Module.Ps3Iso.Bridge;

[TestClass]
public class RenameDecIsoTests : Ps3IsoTestBase
{
    // --- Happy path ---

    [TestMethod]
    public async Task RenameDecIso_RenamesFile()
    {
        var pipeline = CreatePipeline();
        var dir = Tmp.CreateSubDir("completed");
        var filePath = Path.Combine(dir, "Skate 3 - BLES-00760.dec.iso");
        TempDirectory.CreateFile(dir, "Skate 3 - BLES-00760.dec.iso", 4096);

        await pipeline.RenameDecIsoAsync(filePath);

        Assert.IsFalse(File.Exists(filePath), ".dec.iso should no longer exist");
        Assert.IsTrue(File.Exists(Path.Combine(dir, "Skate 3 - BLES-00760.iso")));
    }

    [TestMethod]
    public async Task RenameDecIso_PreservesContent()
    {
        var pipeline = CreatePipeline();
        var dir = Tmp.CreateSubDir("completed");
        var content = new byte[2048];
        Random.Shared.NextBytes(content);
        var filePath = Path.Combine(dir, "Game.dec.iso");
        File.WriteAllBytes(filePath, content);

        await pipeline.RenameDecIsoAsync(filePath);

        var renamed = File.ReadAllBytes(Path.Combine(dir, "Game.iso"));
        CollectionAssert.AreEqual(content, renamed);
    }

    [TestMethod]
    public async Task RenameDecIso_EmitsDoneStatus()
    {
        var pipeline = CreatePipeline();
        var dir = Tmp.CreateSubDir("completed");
        TempDirectory.CreateFile(dir, "Game.dec.iso", 1024);

        await pipeline.RenameDecIsoAsync(Path.Combine(dir, "Game.dec.iso"));

        var done = Bridge.StatusEvents.LastOrDefault(s => s.Phase == "done");
        Assert.IsNotNull(done);
        Assert.AreEqual("Game.dec.iso", done.ZipName);
        Assert.AreEqual("Game.iso", done.IsoFilename);
        StringAssert.Contains(done.Message, "ISO ready");
    }

    [TestMethod]
    public async Task RenameDecIso_MarksAsConverted()
    {
        var pipeline = CreatePipeline();
        var dir = Tmp.CreateSubDir("completed");
        TempDirectory.CreateFile(dir, "Game.dec.iso", 1024);

        await pipeline.RenameDecIsoAsync(Path.Combine(dir, "Game.dec.iso"));

        Assert.IsTrue(pipeline.IsConverted("Game.dec.iso"));
    }

    // --- Target already exists ---

    [TestMethod]
    public async Task RenameDecIso_TargetExists_Overwrites()
    {
        var pipeline = CreatePipeline();
        var dir = Tmp.CreateSubDir("completed");

        var newContent = new byte[2048];
        Random.Shared.NextBytes(newContent);
        File.WriteAllBytes(Path.Combine(dir, "Game.dec.iso"), newContent);
        File.WriteAllBytes(Path.Combine(dir, "Game.iso"), new byte[64]); // old stale file

        await pipeline.RenameDecIsoAsync(Path.Combine(dir, "Game.dec.iso"));

        var result = File.ReadAllBytes(Path.Combine(dir, "Game.iso"));
        CollectionAssert.AreEqual(newContent, result);
    }

    // --- Not a .dec.iso ---

    [TestMethod]
    public async Task RenameDecIso_NotDecIso_NoOp()
    {
        var pipeline = CreatePipeline();
        var dir = Tmp.CreateSubDir("completed");
        TempDirectory.CreateFile(dir, "Game.iso", 1024);

        await pipeline.RenameDecIsoAsync(Path.Combine(dir, "Game.iso"));

        // File should still exist, no rename happened
        Assert.IsTrue(File.Exists(Path.Combine(dir, "Game.iso")));
        Assert.AreEqual(0, Bridge.StatusEvents.Count);
    }

    [TestMethod]
    public async Task RenameDecIso_RegularZip_NoOp()
    {
        var pipeline = CreatePipeline();
        var dir = Tmp.CreateSubDir("completed");
        TempDirectory.CreateFile(dir, "Game.7z", 1024);

        await pipeline.RenameDecIsoAsync(Path.Combine(dir, "Game.7z"));

        Assert.IsTrue(File.Exists(Path.Combine(dir, "Game.7z")));
        Assert.AreEqual(0, Bridge.StatusEvents.Count);
    }

    // --- Source missing ---

    [TestMethod]
    public async Task RenameDecIso_FileDoesNotExist_EmitsError()
    {
        var pipeline = CreatePipeline();
        var dir = Tmp.CreateSubDir("completed");

        await pipeline.RenameDecIsoAsync(Path.Combine(dir, "Missing.dec.iso"));

        var error = Bridge.StatusEvents.LastOrDefault(s => s.Phase == "error");
        Assert.IsNotNull(error);
    }

    // --- Special filenames ---

    [TestMethod]
    public async Task RenameDecIso_SpecialCharsInName()
    {
        var pipeline = CreatePipeline();
        var dir = Tmp.CreateSubDir("completed");
        var name = "God of War\u00ae III - BCES-00510.dec.iso";
        TempDirectory.CreateFile(dir, name, 1024);

        await pipeline.RenameDecIsoAsync(Path.Combine(dir, name));

        Assert.IsFalse(File.Exists(Path.Combine(dir, name)));
        Assert.IsTrue(File.Exists(Path.Combine(dir, "God of War\u00ae III - BCES-00510.iso")));
    }

    [TestMethod]
    public async Task RenameDecIso_CaseInsensitiveSuffix()
    {
        var pipeline = CreatePipeline();
        var dir = Tmp.CreateSubDir("completed");
        TempDirectory.CreateFile(dir, "Game.DEC.ISO", 1024);

        await pipeline.RenameDecIsoAsync(Path.Combine(dir, "Game.DEC.ISO"));

        Assert.IsTrue(
            File.Exists(Path.Combine(dir, "Game.iso")) ||
            !File.Exists(Path.Combine(dir, "Game.DEC.ISO")),
            "Should handle case-insensitive suffix");
    }

    // --- EmitStatus events ---

    [TestMethod]
    public async Task RenameDecIso_EmitsConvertingThenDone()
    {
        var pipeline = CreatePipeline();
        var dir = Tmp.CreateSubDir("completed");
        TempDirectory.CreateFile(dir, "Game.dec.iso", 1024);

        await pipeline.RenameDecIsoAsync(Path.Combine(dir, "Game.dec.iso"));

        var statuses = Bridge.StatusEvents;
        Assert.IsTrue(statuses.Count >= 2);
        Assert.AreEqual("converting", statuses[0].Phase);
        Assert.AreEqual("done", statuses[^1].Phase);
    }
}
