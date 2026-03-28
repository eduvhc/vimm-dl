using Module.Extractor.Tests.Helpers;

[TestClass]
public class ZipExtractTests : ExtractorTestBase
{
    [TestMethod]
    public async Task QuickCheck_FileNotFound_ReturnsFalse()
    {
        var bogus = Path.Combine(Tmp.Root, "nonexistent.7z");

        var result = await ZipExtract.QuickCheckAsync(bogus);

        Assert.IsFalse(result.IsOk);
        Assert.IsNotNull(result.Error);
        StringAssert.Contains(result.Error, "not found");
    }

    [TestMethod]
    public async Task Extract_FileNotFound_ReturnsFalse()
    {
        var bogus = Path.Combine(Tmp.Root, "nonexistent.7z");
        var outDir = Tmp.CreateSubDir("output");

        var result = await ZipExtract.ExtractAsync(bogus, outDir);

        Assert.IsFalse(result.IsOk);
        Assert.IsNotNull(result.Error);
        StringAssert.Contains(result.Error, "not found");
    }
}
