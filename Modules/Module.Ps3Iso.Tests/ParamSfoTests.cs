using Module.Ps3Iso.Tests.Helpers;

[TestClass]
public class ParamSfoTests : Ps3IsoTestBase
{
    [TestMethod]
    public void Parse_ValidSfo_ExtractsTitleAndId()
    {
        var sfoPath = Path.Combine(Tmp.Root, "PARAM.SFO");
        File.WriteAllBytes(sfoPath, BuildParamSfo("God of War III", "BCES00510"));

        var result = ParamSfo.Parse(sfoPath);

        Assert.IsNotNull(result);
        Assert.AreEqual("God of War III", result.Title);
        Assert.AreEqual("BCES-00510", result.TitleId);
    }

    [TestMethod]
    public void Parse_TitleIdWithDash_PreservesDash()
    {
        var sfoPath = Path.Combine(Tmp.Root, "PARAM.SFO");
        File.WriteAllBytes(sfoPath, BuildParamSfo("Test", "BLES-01807"));

        var result = ParamSfo.Parse(sfoPath);

        Assert.IsNotNull(result);
        Assert.AreEqual("BLES-01807", result.TitleId);
    }

    [TestMethod]
    public void Parse_TitleIdWithoutDash_InsertsDash()
    {
        var sfoPath = Path.Combine(Tmp.Root, "PARAM.SFO");
        File.WriteAllBytes(sfoPath, BuildParamSfo("Test", "BLES01807"));

        var result = ParamSfo.Parse(sfoPath);

        Assert.IsNotNull(result);
        Assert.AreEqual("BLES-01807", result.TitleId);
    }

    [TestMethod]
    public void Parse_InvalidMagic_ReturnsNull()
    {
        var sfoPath = Path.Combine(Tmp.Root, "PARAM.SFO");
        File.WriteAllBytes(sfoPath, [0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF]);

        Assert.IsNull(ParamSfo.Parse(sfoPath));
    }

    [TestMethod]
    public void Parse_TooSmall_ReturnsNull()
    {
        var sfoPath = Path.Combine(Tmp.Root, "PARAM.SFO");
        File.WriteAllBytes(sfoPath, [0x00, 0x50, 0x53]);

        Assert.IsNull(ParamSfo.Parse(sfoPath));
    }

    [TestMethod]
    public void Parse_EmptyFile_ReturnsNull()
    {
        var sfoPath = Path.Combine(Tmp.Root, "PARAM.SFO");
        File.WriteAllBytes(sfoPath, []);

        Assert.IsNull(ParamSfo.Parse(sfoPath));
    }

    [TestMethod]
    public void Parse_UnicodeTitle_Preserved()
    {
        var sfoPath = Path.Combine(Tmp.Root, "PARAM.SFO");
        File.WriteAllBytes(sfoPath, BuildParamSfo("God of War\u00ae Collection", "BCES00791"));

        var result = ParamSfo.Parse(sfoPath);

        Assert.IsNotNull(result);
        Assert.AreEqual("God of War\u00ae Collection", result.Title);
    }

    [TestMethod]
    public void Parse_TitleWithSpaces_Trimmed()
    {
        var sfoPath = Path.Combine(Tmp.Root, "PARAM.SFO");
        File.WriteAllBytes(sfoPath, BuildParamSfo("  Skate 3  ", "BLES00760"));

        var result = ParamSfo.Parse(sfoPath);

        Assert.IsNotNull(result);
        Assert.AreEqual("Skate 3", result.Title);
    }
}
