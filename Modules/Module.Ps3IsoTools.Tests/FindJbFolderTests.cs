using Module.Ps3IsoTools.Tests.Helpers;

[TestClass]
public class FindJbFolderTests : Ps3ToolsTestBase
{
    [TestMethod]
    public void FindJbFolder_AtRoot_Found()
    {
        var root = Tmp.CreateSubDir("archive");
        CreateJbFolder(root);
        Assert.IsNotNull(Ps3IsoConverter.FindJbFolder(root));
    }

    [TestMethod]
    public void FindJbFolder_Empty_ReturnsNull()
    {
        var root = Tmp.CreateSubDir("empty");
        Assert.IsNull(Ps3IsoConverter.FindJbFolder(root));
    }

    [TestMethod]
    public void FindJbFolder_OneDeep_Found()
    {
        var root = Tmp.CreateSubDir("archive");
        var sub = Path.Combine(root, "SUBDIR");
        Directory.CreateDirectory(sub);
        CreateJbFolder(sub);
        Assert.IsNotNull(Ps3IsoConverter.FindJbFolder(root));
    }
}
