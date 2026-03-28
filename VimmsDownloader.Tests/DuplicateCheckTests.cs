using Microsoft.Data.Sqlite;

namespace VimmsDownloader.Tests;

/// <summary>
/// Tests for duplicate URL detection across queued and completed tables.
/// Uses in-memory SQLite + temp directory to validate the DB query logic
/// (QueueRepository) and filesystem enrichment logic (DownloadEndpoints).
///
/// PS3 has two pipelines with different file patterns:
///   JB Folder (format=0): .7z → extract PS3_GAME → makeps3iso → .iso (archive never auto-deleted)
///   Dec ISO   (format>0): .dec.iso renamed in-place, or .7z → extract .dec.iso → rename (archive optionally deleted)
/// </summary>
[TestClass]
public class DuplicateCheckTests
{
    private SqliteConnection _db = null!;
    private string _completedDir = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _db = new SqliteConnection("Data Source=:memory:");
        await _db.OpenAsync();
        await ExecAsync("""
            CREATE TABLE queued_urls (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                url TEXT NOT NULL,
                format INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE completed_urls (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                url TEXT NOT NULL,
                filename TEXT NOT NULL,
                filepath TEXT,
                completed_at TEXT,
                conv_phase TEXT,
                conv_message TEXT,
                iso_filename TEXT
            );
            CREATE TABLE url_meta (
                url TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                platform TEXT NOT NULL,
                size TEXT NOT NULL,
                formats TEXT,
                serial TEXT
            );
        """);

        _completedDir = Path.Combine(Path.GetTempPath(), $"dup-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_completedDir);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _db.DisposeAsync();
        if (Directory.Exists(_completedDir))
            Directory.Delete(_completedDir, true);
    }

    // ========================================================================
    // DB query tests
    // ========================================================================

    [TestMethod]
    public async Task NoDuplicates_ReturnsEmpty()
    {
        var result = await CheckDuplicatesDbAsync(["https://vimm.net/vault/1001", "https://vimm.net/vault/1002"]);
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public async Task UrlAlreadyInQueue_DetectedAsDuplicate()
    {
        await InsertQueued("https://vimm.net/vault/1001", 0);
        var result = await CheckDuplicatesDbAsync(["https://vimm.net/vault/1001"]);
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("queued", result[0].Source);
    }

    [TestMethod]
    public async Task UrlInQueue_CaseInsensitive()
    {
        await InsertQueued("https://vimm.net/vault/1001", 0);
        var result = await CheckDuplicatesDbAsync(["HTTPS://VIMM.NET/VAULT/1001"]);
        Assert.AreEqual(1, result.Count);
    }

    [TestMethod]
    public async Task SameUrlQueuedTwice_OnlyOneMatch()
    {
        await InsertQueued("https://vimm.net/vault/1001", 0);
        await InsertQueued("https://vimm.net/vault/1001", 1);
        var result = await CheckDuplicatesDbAsync(["https://vimm.net/vault/1001"]);
        Assert.AreEqual(1, result.Count);
    }

    [TestMethod]
    public async Task CompletedMultipleTimes_ReturnsBestPhase()
    {
        await InsertCompleted("https://vimm.net/vault/1001", "Game.7z");
        await InsertCompleted("https://vimm.net/vault/1001", "Game.7z",
            convPhase: "done", isoFilename: "Game.iso");
        var result = await CheckDuplicatesDbAsync(["https://vimm.net/vault/1001"]);
        var completed = result.Where(r => r.Source == "completed").ToList();
        Assert.AreEqual("done", completed[0].ConvPhase);
    }

    [TestMethod]
    public async Task UrlInBothQueueAndCompleted_ReturnsBothMatches()
    {
        await InsertQueued("https://vimm.net/vault/1001", 1);
        await InsertCompleted("https://vimm.net/vault/1001", "Game.7z");
        var result = await CheckDuplicatesDbAsync(["https://vimm.net/vault/1001"]);
        Assert.AreEqual(2, result.Count);
        Assert.IsTrue(result.Any(r => r.Source == "queued"));
        Assert.IsTrue(result.Any(r => r.Source == "completed"));
    }

    [TestMethod]
    public async Task MixedBatch_SomeDuplicatesSomeNew()
    {
        await InsertQueued("https://vimm.net/vault/1001", 0);
        await InsertCompleted("https://vimm.net/vault/1002", "Game2.7z", convPhase: "done", isoFilename: "Game2.iso");
        var result = await CheckDuplicatesDbAsync([
            "https://vimm.net/vault/1001",
            "https://vimm.net/vault/1002",
            "https://vimm.net/vault/1003",
        ]);
        Assert.AreEqual(2, result.Count);
    }

    [TestMethod]
    public async Task LargeBatch_AllDuplicates()
    {
        for (int i = 1; i <= 50; i++)
            await InsertCompleted($"https://vimm.net/vault/{i}", $"Game{i}.7z");
        var urls = Enumerable.Range(1, 50).Select(i => $"https://vimm.net/vault/{i}").ToList();
        var result = await CheckDuplicatesDbAsync(urls);
        Assert.AreEqual(50, result.Count);
    }

    [TestMethod]
    public async Task EmptyUrlList_ReturnsEmpty()
    {
        Assert.AreEqual(0, (await CheckDuplicatesDbAsync([])).Count);
    }

    [TestMethod]
    public async Task DuplicateUrlsInInput_EachMatchedOnce()
    {
        await InsertCompleted("https://vimm.net/vault/1001", "Game.7z");
        var result = await CheckDuplicatesDbAsync(["https://vimm.net/vault/1001", "https://vimm.net/vault/1001"]);
        Assert.AreEqual(1, result.Count);
    }

    [TestMethod]
    public async Task UrlWithTrailingSlash_TreatedAsDifferent()
    {
        await InsertQueued("https://vimm.net/vault/1001", 0);
        Assert.AreEqual(0, (await CheckDuplicatesDbAsync(["https://vimm.net/vault/1001/"])).Count);
    }

    [TestMethod]
    public async Task MetadataTitle_IncludedInResult()
    {
        await InsertCompleted("https://vimm.net/vault/1001", "TLOU.7z");
        // Override the auto-inserted meta with a proper title
        await InsertMeta("https://vimm.net/vault/1001", "The Last of Us", "PlayStation 3");
        var result = await CheckDuplicatesDbAsync(["https://vimm.net/vault/1001"]);
        Assert.AreEqual("The Last of Us", result[0].Title);
    }

    // ========================================================================
    // JB Folder pipeline (format=0): .7z → extract → makeps3iso → .iso
    // Archive is NEVER auto-deleted. ISO name comes from PARAM.SFO.
    // ========================================================================

    [TestMethod]
    public async Task JbFolder_ConversionDone_BothFilesOnDisk()
    {
        // Typical JB Folder success: archive + ISO both in completed/
        await InsertCompleted("https://vimm.net/vault/2001", "Uncharted 2 (USA).7z",
            convPhase: "done", isoFilename: "Uncharted 2 - BCUS98190.iso");
        CreateFile("Uncharted 2 (USA).7z");
        CreateFile("Uncharted 2 - BCUS98190.iso");

        var result = await CheckDuplicatesFullAsync(["https://vimm.net/vault/2001"]);

        Assert.AreEqual(1, result.Count);
        Assert.IsTrue(result[0].ArchiveExists);
        Assert.IsTrue(result[0].IsoExists);
        Assert.AreEqual("Already converted to ISO (archive + ISO on disk)", result[0].Reason);
    }

    [TestMethod]
    public async Task JbFolder_ConversionDone_UserDeletedIso()
    {
        // User deleted the ISO from completed/ — archive still there, can re-convert
        await InsertCompleted("https://vimm.net/vault/2001", "Uncharted 2 (USA).7z",
            convPhase: "done", isoFilename: "Uncharted 2 - BCUS98190.iso");
        CreateFile("Uncharted 2 (USA).7z");
        // ISO not on disk

        var result = await CheckDuplicatesFullAsync(["https://vimm.net/vault/2001"]);

        Assert.AreEqual(1, result.Count);
        Assert.IsTrue(result[0].ArchiveExists, "Archive still on disk");
        Assert.IsFalse(result[0].IsoExists, "ISO was deleted");
        // conv_phase is "done" but ISO is missing — falls through to "archiveExists" reason
        Assert.AreEqual("Already downloaded", result[0].Reason);
    }

    [TestMethod]
    public async Task JbFolder_ConversionDone_UserDeletedBothFiles()
    {
        // User deleted everything — free to re-download
        await InsertCompleted("https://vimm.net/vault/2001", "Uncharted 2 (USA).7z",
            convPhase: "done", isoFilename: "Uncharted 2 - BCUS98190.iso");

        var result = await CheckDuplicatesFullAsync(["https://vimm.net/vault/2001"]);

        Assert.AreEqual(0, result.Count, "No files on disk → not a duplicate");
    }

    [TestMethod]
    public async Task JbFolder_Extracting_AlwaysBlocks()
    {
        // JB folder being extracted in ps3_temp/ — archive is in completed/
        // Must block even though ISO doesn't exist yet
        await InsertCompleted("https://vimm.net/vault/2001", "Uncharted 2 (USA).7z",
            convPhase: "extracting", convMessage: "42%");
        CreateFile("Uncharted 2 (USA).7z");

        var result = await CheckDuplicatesFullAsync(["https://vimm.net/vault/2001"]);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("Already downloaded (conversion in progress)", result[0].Reason);
    }

    [TestMethod]
    public async Task JbFolder_Extracted_WaitingForConvert_AlwaysBlocks()
    {
        // JB folder extracted, waiting for convert worker — must block
        await InsertCompleted("https://vimm.net/vault/2001", "Uncharted 2 (USA).7z",
            convPhase: "extracted");
        CreateFile("Uncharted 2 (USA).7z");

        var result = await CheckDuplicatesFullAsync(["https://vimm.net/vault/2001"]);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("Already downloaded (conversion in progress)", result[0].Reason);
    }

    [TestMethod]
    public async Task JbFolder_Converting_MakingIso_AlwaysBlocks()
    {
        // makeps3iso running — must block
        await InsertCompleted("https://vimm.net/vault/2001", "Uncharted 2 (USA).7z",
            convPhase: "converting", convMessage: "Creating ISO");
        CreateFile("Uncharted 2 (USA).7z");

        var result = await CheckDuplicatesFullAsync(["https://vimm.net/vault/2001"]);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("Already downloaded (conversion in progress)", result[0].Reason);
    }

    [TestMethod]
    public async Task JbFolder_Queued_WaitingToStart_AlwaysBlocks()
    {
        // Queued for conversion — must block
        await InsertCompleted("https://vimm.net/vault/2001", "Uncharted 2 (USA).7z",
            convPhase: "queued");
        CreateFile("Uncharted 2 (USA).7z");

        var result = await CheckDuplicatesFullAsync(["https://vimm.net/vault/2001"]);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("Already downloaded (conversion in progress)", result[0].Reason);
    }

    [TestMethod]
    public async Task JbFolder_ActiveConversion_NoArchiveOnDisk_StillBlocks()
    {
        // Edge case: archive somehow deleted during active conversion
        // (e.g., user deleted it, or disk issue). Pipeline is still working.
        // Must still block — pipeline has state in ps3_temp/
        await InsertCompleted("https://vimm.net/vault/2001", "Uncharted 2 (USA).7z",
            convPhase: "extracting");
        // No files on disk at all

        var result = await CheckDuplicatesFullAsync(["https://vimm.net/vault/2001"]);

        Assert.AreEqual(1, result.Count, "Active conversion always blocks");
        Assert.AreEqual("Already downloaded (conversion in progress)", result[0].Reason);
    }

    [TestMethod]
    public async Task JbFolder_ConversionError_ArchiveExists_CanRetry()
    {
        // Conversion failed but archive still there — warn user
        await InsertCompleted("https://vimm.net/vault/2001", "Uncharted 2 (USA).7z",
            convPhase: "error", convMessage: "makeps3iso failed");
        CreateFile("Uncharted 2 (USA).7z");

        var result = await CheckDuplicatesFullAsync(["https://vimm.net/vault/2001"]);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("Already downloaded (conversion failed)", result[0].Reason);
    }

    [TestMethod]
    public async Task JbFolder_NotYetConverted_ArchiveExists()
    {
        // Downloaded but conversion hasn't started (non-PS3 or user hasn't clicked Convert)
        await InsertCompleted("https://vimm.net/vault/2001", "Uncharted 2 (USA).7z");
        CreateFile("Uncharted 2 (USA).7z");

        var result = await CheckDuplicatesFullAsync(["https://vimm.net/vault/2001"]);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("Already downloaded", result[0].Reason);
    }

    // ========================================================================
    // Dec ISO pipeline (format>0)
    // Naked .dec.iso: renamed in-place (original filename gone)
    // Archive with .dec.iso: extracted + renamed (archive optionally deleted)
    // ========================================================================

    [TestMethod]
    public async Task DecIso_Naked_Renamed_OnlyIsoOnDisk()
    {
        // .dec.iso was renamed in-place. Original filename no longer exists.
        await InsertCompleted("https://vimm.net/vault/3001", "Uncharted 2 (USA).dec.iso",
            convPhase: "done", isoFilename: "Uncharted 2 - BCUS98190.iso");
        // Original .dec.iso doesn't exist — it was renamed
        CreateFile("Uncharted 2 - BCUS98190.iso");

        var result = await CheckDuplicatesFullAsync(["https://vimm.net/vault/3001"]);

        Assert.AreEqual(1, result.Count);
        Assert.IsFalse(result[0].ArchiveExists, "Original .dec.iso was renamed away");
        Assert.IsTrue(result[0].IsoExists, "Renamed ISO exists");
        Assert.AreEqual("Already converted to ISO", result[0].Reason);
    }

    [TestMethod]
    public async Task DecIso_Naked_UserDeletedRenamedIso()
    {
        // .dec.iso was renamed, then user deleted the renamed file
        await InsertCompleted("https://vimm.net/vault/3001", "Uncharted 2 (USA).dec.iso",
            convPhase: "done", isoFilename: "Uncharted 2 - BCUS98190.iso");
        // Nothing on disk

        var result = await CheckDuplicatesFullAsync(["https://vimm.net/vault/3001"]);

        Assert.AreEqual(0, result.Count, "No files on disk → free to re-download");
    }

    [TestMethod]
    public async Task DecIso_Archive_PreserveTrue_BothOnDisk()
    {
        // Archive preserved, ISO extracted and renamed
        await InsertCompleted("https://vimm.net/vault/3002", "Killzone 3 (Europe).7z",
            convPhase: "done", isoFilename: "Killzone 3 - BCES01007.iso");
        CreateFile("Killzone 3 (Europe).7z");
        CreateFile("Killzone 3 - BCES01007.iso");

        var result = await CheckDuplicatesFullAsync(["https://vimm.net/vault/3002"]);

        Assert.AreEqual(1, result.Count);
        Assert.IsTrue(result[0].ArchiveExists);
        Assert.IsTrue(result[0].IsoExists);
        Assert.AreEqual("Already converted to ISO (archive + ISO on disk)", result[0].Reason);
    }

    [TestMethod]
    public async Task DecIso_Archive_PreserveFalse_OnlyIsoOnDisk()
    {
        // Archive deleted after extraction, ISO remains
        await InsertCompleted("https://vimm.net/vault/3002", "Killzone 3 (Europe).7z",
            convPhase: "done", isoFilename: "Killzone 3 - BCES01007.iso");
        // Archive deleted (preserve_archive=false)
        CreateFile("Killzone 3 - BCES01007.iso");

        var result = await CheckDuplicatesFullAsync(["https://vimm.net/vault/3002"]);

        Assert.AreEqual(1, result.Count);
        Assert.IsFalse(result[0].ArchiveExists);
        Assert.IsTrue(result[0].IsoExists);
        Assert.AreEqual("Already converted to ISO", result[0].Reason);
    }

    [TestMethod]
    public async Task DecIso_Archive_Extracting_AlwaysBlocks()
    {
        // .7z being extracted for .dec.iso — must block
        await InsertCompleted("https://vimm.net/vault/3002", "Killzone 3 (Europe).7z",
            convPhase: "extracting", convMessage: "15%");
        CreateFile("Killzone 3 (Europe).7z");

        var result = await CheckDuplicatesFullAsync(["https://vimm.net/vault/3002"]);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("Already downloaded (conversion in progress)", result[0].Reason);
    }

    [TestMethod]
    public async Task DecIso_Archive_ActiveConversion_NoArchive_StillBlocks()
    {
        // Archive deleted while extraction is in progress (edge case)
        await InsertCompleted("https://vimm.net/vault/3002", "Killzone 3 (Europe).7z",
            convPhase: "extracting");

        var result = await CheckDuplicatesFullAsync(["https://vimm.net/vault/3002"]);

        Assert.AreEqual(1, result.Count, "Active conversion always blocks");
    }

    // ========================================================================
    // Cross-pipeline scenarios
    // ========================================================================

    [TestMethod]
    public async Task BatchWithBothPipelines_MixedState()
    {
        // JB Folder: done, both files on disk
        await InsertCompleted("https://vimm.net/vault/2001", "Uncharted.7z",
            convPhase: "done", isoFilename: "Uncharted - BCUS98190.iso");
        CreateFile("Uncharted.7z");
        CreateFile("Uncharted - BCUS98190.iso");

        // Dec ISO: done, only renamed ISO on disk (naked .dec.iso path)
        await InsertCompleted("https://vimm.net/vault/3001", "Killzone.dec.iso",
            convPhase: "done", isoFilename: "Killzone - BCES01007.iso");
        CreateFile("Killzone - BCES01007.iso");

        // Completed but files moved off disk — not a duplicate
        await InsertCompleted("https://vimm.net/vault/4001", "InFamous.7z",
            convPhase: "done", isoFilename: "InFamous - BCUS98119.iso");

        // Brand new URL
        var result = await CheckDuplicatesFullAsync([
            "https://vimm.net/vault/2001",
            "https://vimm.net/vault/3001",
            "https://vimm.net/vault/4001",
            "https://vimm.net/vault/5001",
        ]);

        Assert.AreEqual(2, result.Count, "Only items with files on disk");
        Assert.IsTrue(result.Any(r => r.Url == "https://vimm.net/vault/2001"));
        Assert.IsTrue(result.Any(r => r.Url == "https://vimm.net/vault/3001"));
    }

    // ========================================================================
    // Non-PS3 platform (generic fallback)
    // ========================================================================

    [TestMethod]
    public async Task NonPS3_ArchiveExists_GenericDuplicate()
    {
        await InsertCompleted("https://vimm.net/vault/5001", "MarioKart.zip", platform: "Wii");
        CreateFile("MarioKart.zip");

        var result = await CheckDuplicatesFullAsync(["https://vimm.net/vault/5001"]);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("Already downloaded", result[0].Reason);
        Assert.IsTrue(result[0].ArchiveExists);
    }

    [TestMethod]
    public async Task NonPS3_NoFilesOnDisk_NotDuplicate()
    {
        await InsertCompleted("https://vimm.net/vault/5001", "MarioKart.zip", platform: "Wii");

        var result = await CheckDuplicatesFullAsync(["https://vimm.net/vault/5001"]);

        Assert.AreEqual(0, result.Count, "Non-PS3 with no archive on disk is not a duplicate");
    }

    // ========================================================================
    // Reason string unit tests
    // ========================================================================

    [TestMethod]
    public void BuildReason_ConvDone_OnlyIso()
        => Assert.AreEqual("Already converted to ISO",
            BuildDuplicateReason("done", false, true));

    [TestMethod]
    public void BuildReason_ConvDone_BothFiles()
        => Assert.AreEqual("Already converted to ISO (archive + ISO on disk)",
            BuildDuplicateReason("done", true, true));

    [TestMethod]
    public void BuildReason_ConvDone_OnlyArchive_IsoDeleted()
        => Assert.AreEqual("Already downloaded",
            BuildDuplicateReason("done", true, false));

    [TestMethod]
    public void BuildReason_ConvError_ArchiveExists()
        => Assert.AreEqual("Already downloaded (conversion failed)",
            BuildDuplicateReason("error", true, false));

    [TestMethod]
    public void BuildReason_ConvError_NoArchive()
        => Assert.AreEqual("Already downloaded (conversion failed, archive missing)",
            BuildDuplicateReason("error", false, false));

    [TestMethod]
    public void BuildReason_NoConversion_ArchiveExists()
        => Assert.AreEqual("Already downloaded",
            BuildDuplicateReason(null, true, false));

    [TestMethod]
    public void BuildReason_IsoExistsButPhaseNotDone()
        => Assert.AreEqual("ISO already exists on disk",
            BuildDuplicateReason(null, false, true));

    // ========================================================================
    // Implementation mirror — matches QueueRepository + pipeline delegation
    // ========================================================================

    record DuplicateDbMatch(string Url, string Source, string? ConvPhase, string? Title, string? Filename, string? IsoFilename, string? Platform);
    record DuplicateResult(string Url, string Source, string Reason, string? Title, string? Filename, string? IsoFilename,
        bool ArchiveExists, bool IsoExists);

    /// <summary>DB-only check — mirrors QueueRepository.CheckDuplicatesAsync</summary>
    private async Task<List<DuplicateDbMatch>> CheckDuplicatesDbAsync(List<string> urls)
    {
        if (urls.Count == 0) return [];

        var results = new List<DuplicateDbMatch>();
        var normalized = urls.Select(u => u.ToLowerInvariant()).Distinct().ToList();
        var placeholders = string.Join(",", normalized.Select((_, i) => $"$u{i}"));

        await using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT DISTINCT q.url, m.title, m.platform
                FROM queued_urls q LEFT JOIN url_meta m ON q.url = m.url
                WHERE LOWER(q.url) IN ({placeholders})
            """;
            for (int i = 0; i < normalized.Count; i++)
                cmd.Parameters.AddWithValue($"$u{i}", normalized[i]);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                results.Add(new DuplicateDbMatch(r.GetString(0), "queued", null,
                    r.IsDBNull(1) ? null : r.GetString(1), null, null,
                    r.IsDBNull(2) ? null : r.GetString(2)));
        }

        await using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT c.url, c.conv_phase, c.iso_filename, c.filename, m.title, m.platform
                FROM completed_urls c LEFT JOIN url_meta m ON c.url = m.url
                WHERE LOWER(c.url) IN ({placeholders})
            """;
            for (int i = 0; i < normalized.Count; i++)
                cmd.Parameters.AddWithValue($"$u{i}", normalized[i]);

            var byUrl = new Dictionary<string, DuplicateDbMatch>(StringComparer.OrdinalIgnoreCase);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var url = r.GetString(0);
                var phase = r.IsDBNull(1) ? null : r.GetString(1);
                var iso = r.IsDBNull(2) ? null : r.GetString(2);
                var filename = r.IsDBNull(3) ? null : r.GetString(3);
                var title = r.IsDBNull(4) ? null : r.GetString(4);
                var platform = r.IsDBNull(5) ? null : r.GetString(5);

                if (!byUrl.TryGetValue(url, out var existing) || RankPhase(phase) > RankPhase(existing.ConvPhase))
                    byUrl[url] = new DuplicateDbMatch(url, "completed", phase, title, filename, iso, platform);
            }
            results.AddRange(byUrl.Values);
        }

        return results;
    }

    /// <summary>
    /// DB + filesystem check — mirrors DownloadEndpoints delegation to pipeline.
    /// PS3 items use Ps3ConversionPipeline.CheckDuplicate logic.
    /// Non-PS3 items use generic fallback (archive exists check).
    /// </summary>
    private async Task<List<DuplicateResult>> CheckDuplicatesFullAsync(List<string> urls)
    {
        var dbMatches = await CheckDuplicatesDbAsync(urls);
        var results = new List<DuplicateResult>();

        foreach (var m in dbMatches)
        {
            if (m.Source == "queued")
            {
                results.Add(new DuplicateResult(m.Url, "queued", "Already in download queue",
                    m.Title, null, null, false, false));
                continue;
            }

            // PS3 items — delegate to pipeline-style check (mirrors Ps3ConversionPipeline.CheckDuplicate)
            if (IsPS3(m.Platform))
            {
                var checkResult = Ps3CheckDuplicate(m.Filename, m.IsoFilename, m.ConvPhase);
                if (checkResult == null) continue;
                results.Add(new DuplicateResult(m.Url, "completed", checkResult.Value.Reason,
                    m.Title, m.Filename, m.IsoFilename, checkResult.Value.ArchiveExists, checkResult.Value.IsoExists));
            }
            else
            {
                // Generic fallback for non-pipeline platforms
                var archiveExists = m.Filename != null && File.Exists(Path.Combine(_completedDir, m.Filename));
                if (!archiveExists) continue;
                results.Add(new DuplicateResult(m.Url, "completed", "Already downloaded",
                    m.Title, m.Filename, null, archiveExists, false));
            }
        }

        return results;
    }

    /// <summary>Mirrors Ps3ConversionPipeline.CheckDuplicate</summary>
    private (string Reason, bool ArchiveExists, bool IsoExists)? Ps3CheckDuplicate(
        string? filename, string? isoFilename, string? convPhase)
    {
        // Active conversion — always block
        var isActive = convPhase != null && convPhase is not ("done" or "error" or "skipped");
        if (isActive)
        {
            var archiveOnDisk = filename != null && File.Exists(Path.Combine(_completedDir, filename));
            return ("Already downloaded (conversion in progress)", archiveOnDisk, false);
        }

        // Terminal state — check filesystem
        var archiveExists = filename != null && File.Exists(Path.Combine(_completedDir, filename));
        var isoExists = isoFilename != null && File.Exists(Path.Combine(_completedDir, isoFilename));

        if (!archiveExists && !isoExists) return null;

        var reason = BuildDuplicateReason(convPhase, archiveExists, isoExists);
        return (reason, archiveExists, isoExists);
    }

    private static bool IsPS3(string? platform) =>
        "PlayStation 3".Equals(platform, StringComparison.OrdinalIgnoreCase);

    private static int RankPhase(string? phase) => phase switch
    {
        "done" => 3, "error" => 1, null => 0, _ => 2,
    };

    /// <summary>Mirrors Ps3ConversionPipeline.BuildDuplicateReason</summary>
    private static string BuildDuplicateReason(string? convPhase, bool archiveExists, bool isoExists)
    {
        if (convPhase == "done" && isoExists)
            return archiveExists ? "Already converted to ISO (archive + ISO on disk)" : "Already converted to ISO";
        if (convPhase == "error")
            return archiveExists ? "Already downloaded (conversion failed)" : "Already downloaded (conversion failed, archive missing)";
        if (archiveExists)
            return "Already downloaded";
        if (isoExists)
            return "ISO already exists on disk";
        return "Already downloaded";
    }

    // ========================================================================
    // Helpers
    // ========================================================================

    private async Task InsertQueued(string url, int format)
    {
        await using var cmd = _db.CreateCommand();
        cmd.CommandText = "INSERT INTO queued_urls (url, format) VALUES ($url, $format)";
        cmd.Parameters.AddWithValue("$url", url);
        cmd.Parameters.AddWithValue("$format", format);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task InsertCompleted(string url, string filename,
        string? convPhase = null, string? convMessage = null, string? isoFilename = null,
        string platform = "PlayStation 3")
    {
        await using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO completed_urls (url, filename, filepath, completed_at, conv_phase, conv_message, iso_filename)
            VALUES ($url, $filename, $filepath, datetime('now'), $phase, $msg, $iso)
        """;
        cmd.Parameters.AddWithValue("$url", url);
        cmd.Parameters.AddWithValue("$filename", filename);
        cmd.Parameters.AddWithValue("$filepath", $"/downloads/completed/{filename}");
        cmd.Parameters.AddWithValue("$phase", (object?)convPhase ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$msg", (object?)convMessage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$iso", (object?)isoFilename ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();

        // Also insert metadata with platform for pipeline routing
        await InsertMeta(url, filename, platform);
    }

    private async Task InsertMeta(string url, string title, string platform)
    {
        await using var cmd = _db.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO url_meta (url, title, platform, size) VALUES ($url, $title, $platform, '')";
        cmd.Parameters.AddWithValue("$url", url);
        cmd.Parameters.AddWithValue("$title", title);
        cmd.Parameters.AddWithValue("$platform", platform);
        await cmd.ExecuteNonQueryAsync();
    }

    private void CreateFile(string filename)
        => File.WriteAllText(Path.Combine(_completedDir, filename), "dummy");

    private async Task ExecAsync(string sql)
    {
        await using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }
}
