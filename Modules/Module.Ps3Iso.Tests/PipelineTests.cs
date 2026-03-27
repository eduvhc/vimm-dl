using Module.Ps3Iso.Tests.Helpers;
using Module.Ps3Iso.Bridge;

[TestClass]
public class PipelineTests : Ps3IsoTestBase
{
    // --- Enqueue ---

    [TestMethod]
    public void Enqueue_NewFile_ReturnsTrue()
    {
        var pipeline = CreatePipeline();
        var completedDir = Tmp.CreateSubDir("completed");
        var tempDir = Tmp.CreateSubDir("ps3_temp");
        var zipPath = Path.Combine(completedDir, "game.7z");
        File.WriteAllBytes(zipPath, [0x00]);

        var result = pipeline.Enqueue(zipPath, completedDir, tempDir);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void Enqueue_AlreadyConverted_ReturnsFalse()
    {
        var pipeline = CreatePipeline();
        var completedDir = Tmp.CreateSubDir("completed");
        var tempDir = Tmp.CreateSubDir("ps3_temp");
        var zipPath = Path.Combine(completedDir, "game.7z");
        File.WriteAllBytes(zipPath, [0x00]);

        pipeline.MarkConverted("game.7z");

        var result = pipeline.Enqueue(zipPath, completedDir, tempDir);
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void Enqueue_AlreadyConverted_ForceTrue_ReturnsTrue()
    {
        var pipeline = CreatePipeline();
        var completedDir = Tmp.CreateSubDir("completed");
        var tempDir = Tmp.CreateSubDir("ps3_temp");
        var zipPath = Path.Combine(completedDir, "game.7z");
        File.WriteAllBytes(zipPath, [0x00]);

        pipeline.MarkConverted("game.7z");

        var result = pipeline.Enqueue(zipPath, completedDir, tempDir, force: true);
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void Enqueue_TwoDifferentFiles_BothQueued()
    {
        var pipeline = CreatePipeline();
        var completedDir = Tmp.CreateSubDir("completed");
        var tempDir = Tmp.CreateSubDir("ps3_temp");

        var zip1 = Path.Combine(completedDir, "game1.7z");
        var zip2 = Path.Combine(completedDir, "game2.7z");
        File.WriteAllBytes(zip1, [0x00]);
        File.WriteAllBytes(zip2, [0x00]);

        Assert.IsTrue(pipeline.Enqueue(zip1, completedDir, tempDir));
        Assert.IsTrue(pipeline.Enqueue(zip2, completedDir, tempDir));

        var statuses = pipeline.GetStatuses();
        Assert.AreEqual(2, statuses.Count);
    }

    // --- MarkConverted ---

    [TestMethod]
    public void MarkConverted_SetsStatusToDone()
    {
        var pipeline = CreatePipeline();

        pipeline.MarkConverted("game.7z");

        Assert.IsTrue(pipeline.IsConverted("game.7z"));
        var statuses = pipeline.GetStatuses();
        Assert.AreEqual(1, statuses.Count);
        Assert.AreEqual("done", statuses[0].Phase);
    }

    [TestMethod]
    public void IsConverted_UnknownFile_ReturnsFalse()
    {
        var pipeline = CreatePipeline();

        Assert.IsFalse(pipeline.IsConverted("unknown.7z"));
    }

    // --- Abort ---

    [TestMethod]
    public void Abort_QueuedItem_ReturnsTrue()
    {
        var pipeline = CreatePipeline();
        var completedDir = Tmp.CreateSubDir("completed");
        var tempDir = Tmp.CreateSubDir("ps3_temp");
        var zipPath = Path.Combine(completedDir, "game.7z");
        File.WriteAllBytes(zipPath, [0x00]);

        pipeline.Enqueue(zipPath, completedDir, tempDir);
        var aborted = pipeline.Abort("game.7z");

        Assert.IsTrue(aborted);
    }

    [TestMethod]
    public void Abort_UnknownItem_ReturnsFalse()
    {
        var pipeline = CreatePipeline();

        Assert.IsFalse(pipeline.Abort("unknown.7z"));
    }

    [TestMethod]
    public void Abort_SetsStatusToError()
    {
        var pipeline = CreatePipeline();
        var completedDir = Tmp.CreateSubDir("completed");
        var tempDir = Tmp.CreateSubDir("ps3_temp");
        var zipPath = Path.Combine(completedDir, "game.7z");
        File.WriteAllBytes(zipPath, [0x00]);

        pipeline.Enqueue(zipPath, completedDir, tempDir);
        pipeline.Abort("game.7z");

        var status = pipeline.GetStatuses().First(s => s.ZipName == "game.7z");
        Assert.AreEqual("error", status.Phase);
        StringAssert.Contains(status.Message, "Aborted");
    }

    // --- GetStatuses ---

    [TestMethod]
    public void GetStatuses_Empty_ReturnsEmpty()
    {
        var pipeline = CreatePipeline();

        Assert.AreEqual(0, pipeline.GetStatuses().Count);
    }

    [TestMethod]
    public void GetStatuses_AfterEnqueue_ReturnsQueued()
    {
        var pipeline = CreatePipeline();
        var completedDir = Tmp.CreateSubDir("completed");
        var tempDir = Tmp.CreateSubDir("ps3_temp");
        var zipPath = Path.Combine(completedDir, "game.7z");
        File.WriteAllBytes(zipPath, [0x00]);

        pipeline.Enqueue(zipPath, completedDir, tempDir);

        var statuses = pipeline.GetStatuses();
        Assert.AreEqual(1, statuses.Count);
        Assert.AreEqual("game.7z", statuses[0].ZipName);
        Assert.AreEqual("queued", statuses[0].Phase);
    }

    // --- CleanupOrphans ---

    [TestMethod]
    public void CleanupOrphans_DeletesOrphanedTempDirs()
    {
        var basePath = Tmp.CreateSubDir("downloads");
        var tempDir = Tmp.CreateSubDir("downloads/ps3_temp");
        var orphan = Tmp.CreateSubDir("downloads/ps3_temp/orphan123");
        File.WriteAllText(Path.Combine(orphan, "somefile.txt"), "data");
        Tmp.CreateSubDir("downloads/completed");

        var pipeline = CreatePipeline();
        pipeline.CleanupOrphans(basePath);

        Assert.IsFalse(Directory.Exists(orphan), "Orphaned temp dir should be deleted");
    }

    [TestMethod]
    public void CleanupOrphans_DeletesOrphanedTempIsos()
    {
        var basePath = Tmp.CreateSubDir("downloads");
        var completedDir = Tmp.CreateSubDir("downloads/completed");
        var tempIso = Path.Combine(completedDir, "temp_abc123.iso");
        File.WriteAllBytes(tempIso, [0x00]);

        var pipeline = CreatePipeline();
        pipeline.CleanupOrphans(basePath);

        Assert.IsFalse(File.Exists(tempIso), "Orphaned temp ISO should be deleted");
    }

    [TestMethod]
    public void CleanupOrphans_LoadsConvertedList()
    {
        var basePath = Tmp.CreateSubDir("downloads");
        var completedDir = Tmp.CreateSubDir("downloads/completed");
        File.WriteAllText(Path.Combine(completedDir, ".ps3converted"), "game1.7z\ngame2.7z\n");

        var pipeline = CreatePipeline();
        pipeline.CleanupOrphans(basePath);

        Assert.IsTrue(pipeline.IsConverted("game1.7z"));
        Assert.IsTrue(pipeline.IsConverted("game2.7z"));
        Assert.IsFalse(pipeline.IsConverted("game3.7z"));
    }

    [TestMethod]
    public void CleanupOrphans_NoDirectories_DoesNotThrow()
    {
        var basePath = Tmp.CreateSubDir("empty");

        var pipeline = CreatePipeline();
        pipeline.CleanupOrphans(basePath); // should not throw
    }

    // --- SanitizeFileName ---

    [TestMethod]
    public void SanitizeFileName_RemovesInvalidChars()
    {
        var result = Ps3IsoConverter.SanitizeFileName("Game: The \"Sequel\" <2>");

        Assert.IsFalse(result.Contains(':'));
        Assert.IsFalse(result.Contains('"'));
        Assert.IsFalse(result.Contains('<'));
        Assert.IsFalse(result.Contains('>'));
    }

    [TestMethod]
    public void SanitizeFileName_PreservesValidChars()
    {
        var result = Ps3IsoConverter.SanitizeFileName("God of War III - BCES-00510");

        Assert.AreEqual("God of War III - BCES-00510", result);
    }
}
