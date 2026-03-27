using Module.Ps3IsoTools.Tests.Helpers;

[TestClass]
public class ParamSfoTests : Ps3ToolsTestBase
{
    [TestMethod]
    public void Parse_ValidSfo_ExtractsTitleAndId()
    {
        var dir = Tmp.CreateSubDir("sfo");
        File.WriteAllBytes(Path.Combine(dir, "PARAM.SFO"), BuildParamSfo("God of War III", "BCES00510"));

        var sfo = ParamSfo.Parse(Path.Combine(dir, "PARAM.SFO"));

        Assert.IsNotNull(sfo);
        Assert.AreEqual("God of War III", sfo.Title);
        Assert.AreEqual("BCES-00510", sfo.TitleId);
    }

    [TestMethod]
    public void Parse_DashAlreadyPresent_Preserved()
    {
        var dir = Tmp.CreateSubDir("sfo");
        File.WriteAllBytes(Path.Combine(dir, "PARAM.SFO"), BuildParamSfo("Game", "BLES-01807"));

        var sfo = ParamSfo.Parse(Path.Combine(dir, "PARAM.SFO"));

        Assert.IsNotNull(sfo);
        Assert.AreEqual("BLES-01807", sfo.TitleId);
    }
}
