using Module.Core.Pipeline;
using Module.Core.Testing;
using Module.Ps3Pipeline.Tests.Helpers;

[TestClass]
public class HandleExtractedArchiveTests : Ps3PipelineTestBase
{
    [TestMethod]
    public async Task DecIso_RenamedAndMoved()
    {
        var pipeline = CreatePipeline();
        var completed = Tmp.CreateSubDir("completed");
        var tempDir = Tmp.CreateSubDir("temp");
        var content = new byte[2048];
        Random.Shared.NextBytes(content);
        File.WriteAllBytes(Path.Combine(tempDir, "Game.dec.iso"), content);

        var result = await pipeline.DecIso.HandleExtractedArchive("archive.7z", tempDir, completed);

        Assert.IsTrue(result.IsOk);
        Assert.IsTrue(File.Exists(Path.Combine(completed, "Game.iso")));
        CollectionAssert.AreEqual(content, File.ReadAllBytes(Path.Combine(completed, "Game.iso")));
    }

    [TestMethod]
    public async Task PlainIso_MovedToCompleted()
    {
        var pipeline = CreatePipeline();
        var completed = Tmp.CreateSubDir("completed");
        var tempDir = Tmp.CreateSubDir("temp");
        TempDirectory.CreateFile(tempDir, "Game.iso", 1024);

        var result = await pipeline.DecIso.HandleExtractedArchive("archive.7z", tempDir, completed);

        Assert.IsTrue(result.IsOk);
        Assert.IsTrue(File.Exists(Path.Combine(completed, "Game.iso")));
    }

    [TestMethod]
    public async Task EmptyDir_ReturnsFalse()
    {
        var pipeline = CreatePipeline();
        var completed = Tmp.CreateSubDir("completed");
        var tempDir = Tmp.CreateSubDir("temp");

        var result = await pipeline.DecIso.HandleExtractedArchive("archive.7z", tempDir, completed);

        Assert.IsFalse(result.IsOk);
    }

    [TestMethod]
    public async Task DecIso_TakesPrecedenceOverPlainIso()
    {
        var pipeline = CreatePipeline();
        var completed = Tmp.CreateSubDir("completed");
        var tempDir = Tmp.CreateSubDir("temp");
        TempDirectory.CreateFile(tempDir, "Game.dec.iso", 2048);
        TempDirectory.CreateFile(tempDir, "Other.iso", 1024);

        await pipeline.DecIso.HandleExtractedArchive("archive.7z", tempDir, completed);

        Assert.IsTrue(File.Exists(Path.Combine(completed, "Game.iso")));
        Assert.IsFalse(File.Exists(Path.Combine(completed, "Other.iso")));
    }

    [TestMethod]
    public async Task EmitsDoneAndMarksConverted()
    {
        var pipeline = CreatePipeline();
        var completed = Tmp.CreateSubDir("completed");
        var tempDir = Tmp.CreateSubDir("temp");
        TempDirectory.CreateFile(tempDir, "Game.dec.iso", 1024);

        await pipeline.DecIso.HandleExtractedArchive("archive.7z", tempDir, completed);

        Assert.IsTrue(pipeline.IsConverted("archive.7z"));
        var done = Bridge.StatusEvents.LastOrDefault(s => s.Phase == PipelinePhase.Done);
        Assert.IsNotNull(done);
        Assert.AreEqual("archive.7z", done.ItemName);
    }
}
