using Module.Ps3Iso.Tests.Helpers;

[TestClass]
public class FindJbFolderEdgeCaseTests : Ps3IsoTestBase
{
    [TestMethod]
    public void FindJbFolder_Ps3GameExistsButParamSfoIsDirectory_ReturnsNull()
    {
        var root = Tmp.CreateSubDir("sfo_dir");
        var ps3Game = Path.Combine(root, "PS3_GAME");
        // PARAM.SFO is a directory, not a file
        Directory.CreateDirectory(Path.Combine(ps3Game, "PARAM.SFO"));

        Assert.IsNull(Ps3IsoConverter.FindJbFolder(root));
    }

    [TestMethod]
    public void FindJbFolder_OnlyPs3GameNoSfo_ReturnsNull()
    {
        var root = Tmp.CreateSubDir("no_sfo");
        Directory.CreateDirectory(Path.Combine(root, "PS3_GAME"));

        Assert.IsNull(Ps3IsoConverter.FindJbFolder(root));
    }

    [TestMethod]
    public void FindJbFolder_Ps3GameCaseSensitive_ReturnsNull()
    {
        var root = Tmp.CreateSubDir("case_test");
        var ps3Game = Path.Combine(root, "ps3_game"); // lowercase
        Directory.CreateDirectory(ps3Game);
        File.WriteAllBytes(Path.Combine(ps3Game, "PARAM.SFO"), BuildParamSfo("Test", "BCES00001"));

        // On Windows this finds it (case-insensitive FS), on Linux it won't
        // The test documents the behavior on the current platform
        var result = Ps3IsoConverter.FindJbFolder(root);
        if (OperatingSystem.IsWindows())
            Assert.IsNotNull(result);
        else
            Assert.IsNull(result);
    }

    [TestMethod]
    public void FindJbFolder_EmptySubdirectories_ReturnsNull()
    {
        var root = Tmp.CreateSubDir("empty_subs");
        Tmp.CreateSubDir("empty_subs/a");
        Tmp.CreateSubDir("empty_subs/b");
        Tmp.CreateSubDir("empty_subs/c/d");

        Assert.IsNull(Ps3IsoConverter.FindJbFolder(root));
    }

    [TestMethod]
    public void FindJbFolder_ManySubdirsOneValid_FindsIt()
    {
        var root = Tmp.CreateSubDir("many_subs");
        for (int i = 0; i < 20; i++)
            Tmp.CreateSubDir($"many_subs/decoy{i}");

        // The valid one at 1 level deep
        var valid = Path.Combine(root, "actual_game", "PS3_GAME");
        Directory.CreateDirectory(valid);
        File.WriteAllBytes(Path.Combine(valid, "PARAM.SFO"), BuildParamSfo("Found", "BCES00001"));

        var result = Ps3IsoConverter.FindJbFolder(root);
        Assert.IsNotNull(result);
    }

    [TestMethod]
    public void FindJbFolder_ZeroByteParamSfo_StillReturnsFolder()
    {
        // FindJbFolder only checks File.Exists, not file validity
        var root = Tmp.CreateSubDir("zero_sfo");
        var ps3Game = Path.Combine(root, "PS3_GAME");
        Directory.CreateDirectory(ps3Game);
        File.WriteAllBytes(Path.Combine(ps3Game, "PARAM.SFO"), []);

        var result = Ps3IsoConverter.FindJbFolder(root);
        Assert.IsNotNull(result);
        Assert.AreEqual(root, result);
    }

    [TestMethod]
    public void FindJbFolder_SpecialCharsInPath()
    {
        var root = Tmp.CreateSubDir("special chars & (test)");
        var ps3Game = Path.Combine(root, "PS3_GAME");
        Directory.CreateDirectory(ps3Game);
        File.WriteAllBytes(Path.Combine(ps3Game, "PARAM.SFO"), BuildParamSfo("Test", "BCES00001"));

        var result = Ps3IsoConverter.FindJbFolder(root);
        Assert.IsNotNull(result);
    }

    // --- SanitizeFileName edge cases ---

    [TestMethod]
    public void SanitizeFileName_SlashAndNullAlwaysReplaced()
    {
        // / and \0 are invalid on ALL platforms
        var result = Ps3IsoConverter.SanitizeFileName("Game/Name\0Bad");
        Assert.IsFalse(result.Contains('/'));
        Assert.IsFalse(result.Contains('\0'));
        StringAssert.Contains(result, "Game");
    }

    [TestMethod]
    public void SanitizeFileName_NoInvalidCharsInResult()
    {
        var input = "Game: The \"Sequel\" <2>/\\|?*";
        var result = Ps3IsoConverter.SanitizeFileName(input);
        var invalid = Path.GetInvalidFileNameChars();
        Assert.IsFalse(result.Any(c => invalid.Contains(c)),
            $"Result '{result}' contains invalid chars for this platform");
        StringAssert.Contains(result, "Game");
    }

    [TestMethod]
    public void SanitizeFileName_EmptyString_ReturnsEmpty()
    {
        Assert.AreEqual("", Ps3IsoConverter.SanitizeFileName(""));
    }

    [TestMethod]
    public void SanitizeFileName_NormalName_Unchanged()
    {
        Assert.AreEqual("God of War III - BCES-00510", Ps3IsoConverter.SanitizeFileName("God of War III - BCES-00510"));
    }

    [TestMethod]
    public void SanitizeFileName_UnicodePreserved()
    {
        Assert.AreEqual("God of War\u00ae III", Ps3IsoConverter.SanitizeFileName("God of War\u00ae III"));
    }
}
