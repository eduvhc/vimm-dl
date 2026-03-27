using Module.Extractor.Tests.Helpers;

[TestClass]
public class ZipExtractTests : ExtractorTestBase
{
    [TestMethod]
    public async Task QuickCheck_FileNotFound_ReturnsFalse()
    {
        var bogus = Path.Combine(Tmp.Root, "nonexistent.7z");

        var (valid, error) = await ZipExtract.QuickCheckAsync(bogus);

        Assert.IsFalse(valid);
        Assert.IsNotNull(error);
        StringAssert.Contains(error, "not found");
    }

    [TestMethod]
    public async Task Extract_FileNotFound_ReturnsFalse()
    {
        var bogus = Path.Combine(Tmp.Root, "nonexistent.7z");
        var outDir = Tmp.CreateSubDir("output");

        var (success, error) = await ZipExtract.ExtractAsync(bogus, outDir);

        Assert.IsFalse(success);
        Assert.IsNotNull(error);
        StringAssert.Contains(error, "not found");
    }
}
