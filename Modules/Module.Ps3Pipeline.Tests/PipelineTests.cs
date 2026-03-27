using Module.Core.Pipeline;
using Module.Core.Testing;
using Module.Ps3Pipeline.Tests.Helpers;

[TestClass]
public class PipelineTests : Ps3PipelineTestBase
{
    [TestMethod]
    public void Enqueue_NewItem_ReturnsTrue()
    {
        var pipeline = CreatePipeline();
        var dir = Tmp.CreateSubDir("completed");
        var tempDir = Tmp.CreateSubDir("temp");
        TempDirectory.CreateFile(dir, "game.7z", 1024);

        var result = pipeline.Enqueue(Path.Combine(dir, "game.7z"), dir, tempDir);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void Enqueue_AlreadyConverted_ReturnsFalse()
    {
        var pipeline = CreatePipeline();
        var dir = Tmp.CreateSubDir("completed");
        var tempDir = Tmp.CreateSubDir("temp");
        TempDirectory.CreateFile(dir, "game.7z", 1024);

        pipeline.MarkConverted("game.7z");
        var result = pipeline.Enqueue(Path.Combine(dir, "game.7z"), dir, tempDir);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void MarkConverted_SetsStatus()
    {
        var pipeline = CreatePipeline();
        pipeline.MarkConverted("game.7z");

        Assert.IsTrue(pipeline.IsConverted("game.7z"));
        var status = pipeline.GetStatuses().Find(s => s.ItemName == "game.7z");
        Assert.IsNotNull(status);
        Assert.AreEqual(PipelinePhase.Done, status.Phase);
    }

    [TestMethod]
    public void Abort_QueuedItem_ReturnsTrue()
    {
        var pipeline = CreatePipeline();
        var dir = Tmp.CreateSubDir("completed");
        var tempDir = Tmp.CreateSubDir("temp");
        TempDirectory.CreateFile(dir, "game.7z", 1024);
        pipeline.Enqueue(Path.Combine(dir, "game.7z"), dir, tempDir);

        var aborted = pipeline.Abort("game.7z");

        Assert.IsTrue(aborted);
    }

    [TestMethod]
    public void Abort_UnknownItem_ReturnsFalse()
    {
        var pipeline = CreatePipeline();
        Assert.IsFalse(pipeline.Abort("nonexistent.7z"));
    }

    [TestMethod]
    public void GetStatuses_ReturnsAll()
    {
        var pipeline = CreatePipeline();
        pipeline.MarkConverted("a.7z");
        pipeline.MarkConverted("b.7z");

        var statuses = pipeline.GetStatuses();
        Assert.AreEqual(2, statuses.Count);
    }

    [TestMethod]
    public void IPipeline_Contract_Works()
    {
        IPipeline pipeline = CreatePipeline();

        pipeline.MarkConverted("test.7z");
        Assert.IsTrue(pipeline.IsConverted("test.7z"));
        Assert.AreEqual(1, pipeline.GetStatuses().Count);
    }
}
