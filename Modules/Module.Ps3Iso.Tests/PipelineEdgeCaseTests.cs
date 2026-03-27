using Module.Ps3Iso.Tests.Helpers;
using Module.Ps3Iso.Bridge;

[TestClass]
public class PipelineEdgeCaseTests : Ps3IsoTestBase
{
    // --- Double operations ---

    [TestMethod]
    public void Abort_SameFileTwice_SecondReturnsFalse()
    {
        var pipeline = CreatePipeline();
        var completedDir = Tmp.CreateSubDir("completed");
        var tempDir = Tmp.CreateSubDir("ps3_temp");
        var zipPath = Path.Combine(completedDir, "game.7z");
        File.WriteAllBytes(zipPath, [0x00]);

        pipeline.Enqueue(zipPath, completedDir, tempDir);
        Assert.IsTrue(pipeline.Abort("game.7z"));
        Assert.IsFalse(pipeline.Abort("game.7z"));
    }

    [TestMethod]
    public void MarkConverted_SameFileTwice_NoError()
    {
        var pipeline = CreatePipeline();

        pipeline.MarkConverted("game.7z");
        pipeline.MarkConverted("game.7z"); // should not throw

        Assert.IsTrue(pipeline.IsConverted("game.7z"));
        Assert.AreEqual(1, pipeline.GetStatuses().Count);
    }

    [TestMethod]
    public void MarkConverted_ThenAbort_StaysDone()
    {
        var pipeline = CreatePipeline();
        pipeline.MarkConverted("game.7z");

        // Abort on a "done" item with no active cancellation token
        Assert.IsFalse(pipeline.Abort("game.7z"));

        // Status should still be done
        var status = pipeline.GetStatuses().First(s => s.ZipName == "game.7z");
        Assert.AreEqual("done", status.Phase);
    }

    // --- Converted list persistence ---

    [TestMethod]
    public void CleanupOrphans_ConvertsListWithBlankLines_IgnoresThem()
    {
        var basePath = Tmp.CreateSubDir("downloads");
        var completedDir = Tmp.CreateSubDir("downloads/completed");
        File.WriteAllText(
            Path.Combine(completedDir, ".ps3converted"),
            "game1.7z\n\n  \ngame2.7z\n\n");

        var pipeline = CreatePipeline();
        pipeline.CleanupOrphans(basePath);

        Assert.IsTrue(pipeline.IsConverted("game1.7z"));
        Assert.IsTrue(pipeline.IsConverted("game2.7z"));
        Assert.IsFalse(pipeline.IsConverted(""));
        Assert.IsFalse(pipeline.IsConverted("  "));
    }

    [TestMethod]
    public void CleanupOrphans_MissingConvertedFile_NoError()
    {
        var basePath = Tmp.CreateSubDir("downloads");
        Tmp.CreateSubDir("downloads/completed");
        // No .ps3converted file exists

        var pipeline = CreatePipeline();
        pipeline.CleanupOrphans(basePath); // should not throw

        Assert.AreEqual(0, pipeline.GetStatuses().Count);
    }

    [TestMethod]
    public void CleanupOrphans_MarkerWithOneLine_SkipsResume()
    {
        var basePath = Tmp.CreateSubDir("downloads");
        var tempBaseDir = Tmp.CreateSubDir("downloads/ps3_temp");
        var orphan = Tmp.CreateSubDir("downloads/ps3_temp/abc123");
        Tmp.CreateSubDir("downloads/completed");

        // Marker with only zip name, missing jb folder line
        File.WriteAllText(Path.Combine(orphan, ".extraction_complete"), "game.7z\n");

        var pipeline = CreatePipeline();
        pipeline.CleanupOrphans(basePath);

        // Orphan should be deleted (can't resume with incomplete marker)
        Assert.IsFalse(Directory.Exists(orphan));
    }

    [TestMethod]
    public void CleanupOrphans_MarkerPointsToNonexistentJbFolder_DeletesOrphan()
    {
        var basePath = Tmp.CreateSubDir("downloads");
        var orphan = Tmp.CreateSubDir("downloads/ps3_temp/def456");
        Tmp.CreateSubDir("downloads/completed");

        File.WriteAllText(
            Path.Combine(orphan, ".extraction_complete"),
            "game.7z\n/nonexistent/path/jb\n");

        var pipeline = CreatePipeline();
        pipeline.CleanupOrphans(basePath);

        Assert.IsFalse(Directory.Exists(orphan));
    }

    // --- Status tracking ---

    [TestMethod]
    public void GetStatuses_AfterMarkConverted_ShowsDone()
    {
        var pipeline = CreatePipeline();
        pipeline.MarkConverted("a.7z");
        pipeline.MarkConverted("b.7z");

        var statuses = pipeline.GetStatuses();
        Assert.AreEqual(2, statuses.Count);
        Assert.IsTrue(statuses.All(s => s.Phase == "done"));
    }

    [TestMethod]
    public void GetStatuses_MixedStates()
    {
        var pipeline = CreatePipeline();
        var completedDir = Tmp.CreateSubDir("completed");
        var tempDir = Tmp.CreateSubDir("ps3_temp");

        pipeline.MarkConverted("done.7z");

        var zipPath = Path.Combine(completedDir, "queued.7z");
        File.WriteAllBytes(zipPath, [0x00]);
        pipeline.Enqueue(zipPath, completedDir, tempDir);

        var statuses = pipeline.GetStatuses();
        Assert.AreEqual(2, statuses.Count);
        Assert.IsTrue(statuses.Any(s => s.Phase == "done" && s.ZipName == "done.7z"));
        Assert.IsTrue(statuses.Any(s => s.Phase == "queued" && s.ZipName == "queued.7z"));
    }

    // --- IsConverted case sensitivity ---

    [TestMethod]
    public void IsConverted_CaseInsensitive()
    {
        var pipeline = CreatePipeline();
        pipeline.MarkConverted("Game.7z");

        Assert.IsTrue(pipeline.IsConverted("Game.7z"));
        Assert.IsTrue(pipeline.IsConverted("game.7z"));
        Assert.IsTrue(pipeline.IsConverted("GAME.7Z"));
    }

    // --- Enqueue edge cases ---

    [TestMethod]
    public void Enqueue_MarkConvertedThenForce_ReQueues()
    {
        var pipeline = CreatePipeline();
        var completedDir = Tmp.CreateSubDir("completed");
        var tempDir = Tmp.CreateSubDir("ps3_temp");
        var zipPath = Path.Combine(completedDir, "game.7z");
        File.WriteAllBytes(zipPath, [0x00]);

        pipeline.MarkConverted("game.7z");
        Assert.IsFalse(pipeline.Enqueue(zipPath, completedDir, tempDir, force: false));
        Assert.IsTrue(pipeline.Enqueue(zipPath, completedDir, tempDir, force: true));
    }

    [TestMethod]
    public void Abort_UnknownFile_ReturnsFalse()
    {
        var pipeline = CreatePipeline();
        Assert.IsFalse(pipeline.Abort("nonexistent.7z"));
    }

    [TestMethod]
    public void Enqueue_MultipleDistinctFiles_AllQueued()
    {
        var pipeline = CreatePipeline();
        var completedDir = Tmp.CreateSubDir("completed");
        var tempDir = Tmp.CreateSubDir("ps3_temp");

        for (int i = 0; i < 10; i++)
        {
            var zipPath = Path.Combine(completedDir, $"game{i}.7z");
            File.WriteAllBytes(zipPath, [0x00]);
            Assert.IsTrue(pipeline.Enqueue(zipPath, completedDir, tempDir));
        }

        Assert.AreEqual(10, pipeline.GetStatuses().Count);
    }
}
