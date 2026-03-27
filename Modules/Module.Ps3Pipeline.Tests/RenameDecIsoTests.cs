using Module.Core.Pipeline;
using Module.Core.Testing;
using Module.Ps3Pipeline.Tests.Helpers;

[TestClass]
public class RenameDecIsoTests : Ps3PipelineTestBase
{
    [TestMethod]
    public async Task RenameDecIso_RenamesFile()
    {
        var pipeline = CreatePipeline();
        var dir = Tmp.CreateSubDir("completed");
        TempDirectory.CreateFile(dir, "Skate 3 - BLES-00760.dec.iso", 4096);

        await pipeline.RenameDecIsoAsync(Path.Combine(dir, "Skate 3 - BLES-00760.dec.iso"));

        Assert.IsFalse(File.Exists(Path.Combine(dir, "Skate 3 - BLES-00760.dec.iso")));
        Assert.IsTrue(File.Exists(Path.Combine(dir, "Skate 3 - BLES-00760.iso")));
    }

    [TestMethod]
    public async Task RenameDecIso_EmitsDoneStatus()
    {
        var pipeline = CreatePipeline();
        var dir = Tmp.CreateSubDir("completed");
        TempDirectory.CreateFile(dir, "Game.dec.iso", 1024);

        await pipeline.RenameDecIsoAsync(Path.Combine(dir, "Game.dec.iso"));

        var done = Bridge.StatusEvents.LastOrDefault(s => s.Phase == PipelinePhase.Done);
        Assert.IsNotNull(done);
        Assert.AreEqual("Game.dec.iso", done.ItemName);
        StringAssert.Contains(done.Message, "ISO ready");
    }

    [TestMethod]
    public async Task RenameDecIso_NotDecIso_NoOp()
    {
        var pipeline = CreatePipeline();
        var dir = Tmp.CreateSubDir("completed");
        TempDirectory.CreateFile(dir, "Game.iso", 1024);

        await pipeline.RenameDecIsoAsync(Path.Combine(dir, "Game.iso"));

        Assert.IsTrue(File.Exists(Path.Combine(dir, "Game.iso")));
        Assert.AreEqual(0, Bridge.StatusEvents.Count);
    }

    [TestMethod]
    public async Task RenameDecIso_WithSerial_FormatsName()
    {
        var pipeline = CreatePipeline();
        var dir = Tmp.CreateSubDir("completed");
        TempDirectory.CreateFile(dir, "Godfather, The (Europe).dec.iso", 1024);

        await pipeline.RenameDecIsoAsync(Path.Combine(dir, "Godfather, The (Europe).dec.iso"), "BLES-00043");

        Assert.IsTrue(File.Exists(Path.Combine(dir, "The Godfather - BLES-00043.iso")));
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
}
