using Module.Ps3Iso.Tests.Helpers;

[TestClass]
public class FindJbFolderTests : Ps3IsoTestBase
{
    [TestMethod]
    public void FindJbFolder_AtRoot_Found()
    {
        var root = Tmp.CreateSubDir("game");
        CreateJbFolder(Path.GetDirectoryName(root)!, "Test", "BCES00001");
        // CreateJbFolder creates GAMEFOLDER/PS3_GAME/PARAM.SFO - we need PS3_GAME at root level
        var ps3Game = Path.Combine(root, "PS3_GAME");
        Directory.CreateDirectory(ps3Game);
        File.WriteAllBytes(Path.Combine(ps3Game, "PARAM.SFO"), BuildParamSfo("Test", "BCES00001"));

        var result = Ps3IsoConverter.FindJbFolder(root);

        Assert.IsNotNull(result);
        Assert.AreEqual(root, result);
    }

    [TestMethod]
    public void FindJbFolder_OneDeep_Found()
    {
        var root = Tmp.CreateSubDir("extracted");
        var gameDir = Path.Combine(root, "GameName");
        var ps3Game = Path.Combine(gameDir, "PS3_GAME");
        Directory.CreateDirectory(ps3Game);
        File.WriteAllBytes(Path.Combine(ps3Game, "PARAM.SFO"), BuildParamSfo("Test", "BCES00001"));

        var result = Ps3IsoConverter.FindJbFolder(root);

        Assert.IsNotNull(result);
        Assert.AreEqual(gameDir, result);
    }

    [TestMethod]
    public void FindJbFolder_TwoDeep_Found()
    {
        var root = Tmp.CreateSubDir("extracted");
        var nested = Path.Combine(root, "Outer", "Inner");
        var ps3Game = Path.Combine(nested, "PS3_GAME");
        Directory.CreateDirectory(ps3Game);
        File.WriteAllBytes(Path.Combine(ps3Game, "PARAM.SFO"), BuildParamSfo("Test", "BCES00001"));

        var result = Ps3IsoConverter.FindJbFolder(root);

        Assert.IsNotNull(result);
        Assert.AreEqual(nested, result);
    }

    [TestMethod]
    public void FindJbFolder_ThreeDeep_NotFound()
    {
        var root = Tmp.CreateSubDir("extracted");
        var deep = Path.Combine(root, "A", "B", "C");
        var ps3Game = Path.Combine(deep, "PS3_GAME");
        Directory.CreateDirectory(ps3Game);
        File.WriteAllBytes(Path.Combine(ps3Game, "PARAM.SFO"), BuildParamSfo("Test", "BCES00001"));

        var result = Ps3IsoConverter.FindJbFolder(root);

        Assert.IsNull(result, "Should not find JB folder 3 levels deep");
    }

    [TestMethod]
    public void FindJbFolder_EmptyDir_ReturnsNull()
    {
        var root = Tmp.CreateSubDir("empty");

        Assert.IsNull(Ps3IsoConverter.FindJbFolder(root));
    }

    [TestMethod]
    public void FindJbFolder_NoParamSfo_ReturnsNull()
    {
        var root = Tmp.CreateSubDir("extracted");
        Directory.CreateDirectory(Path.Combine(root, "PS3_GAME"));
        // PS3_GAME exists but no PARAM.SFO

        Assert.IsNull(Ps3IsoConverter.FindJbFolder(root));
    }

    [TestMethod]
    public void FindJbFolder_MultipleGames_FindsFirst()
    {
        var root = Tmp.CreateSubDir("extracted");

        var game1 = Path.Combine(root, "Game1", "PS3_GAME");
        Directory.CreateDirectory(game1);
        File.WriteAllBytes(Path.Combine(game1, "PARAM.SFO"), BuildParamSfo("First", "BCES00001"));

        var game2 = Path.Combine(root, "Game2", "PS3_GAME");
        Directory.CreateDirectory(game2);
        File.WriteAllBytes(Path.Combine(game2, "PARAM.SFO"), BuildParamSfo("Second", "BCES00002"));

        var result = Ps3IsoConverter.FindJbFolder(root);
        Assert.IsNotNull(result);
    }
}
