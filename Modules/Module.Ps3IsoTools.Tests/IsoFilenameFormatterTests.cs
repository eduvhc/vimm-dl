[TestClass]
public class IsoFilenameFormatterTests
{
    [TestMethod]
    public void FixThePrefix_GodFather()
        => Assert.AreEqual("The Godfather - The Don's Edition",
            IsoFilenameFormatter.FixThePrefix("Godfather, The - The Don's Edition"));

    [TestMethod]
    public void FixThePrefix_LastOfUs()
        => Assert.AreEqual("The Last of Us",
            IsoFilenameFormatter.FixThePrefix("Last of Us, The"));

    [TestMethod]
    public void Format_DecIsoWithRegionAndSerial()
    {
        var result = IsoFilenameFormatter.Format(
            "Godfather, The - The Don's Edition (Europe).dec.iso", "BLES-00043");
        Assert.AreEqual("The Godfather - The Don's Edition - BLES-00043.iso", result);
    }

    [TestMethod]
    public void Format_NoSerial_StripsRegion()
    {
        var result = IsoFilenameFormatter.Format(
            "God of War III (Europe) (En,De,Es).dec.iso", null);
        Assert.AreEqual("God of War III.iso", result);
    }

    [TestMethod]
    public void Format_WithOptions_DisableAll()
    {
        var opts = new IsoRenameOptions(FixThe: false, AddSerial: false, StripRegion: false);
        var result = IsoFilenameFormatter.Format(
            "Godfather, The (Europe).dec.iso", "BLES-00043", opts);
        Assert.AreEqual("Godfather, The (Europe).iso", result);
    }
}
