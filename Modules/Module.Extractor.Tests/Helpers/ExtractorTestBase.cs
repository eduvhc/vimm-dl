using Module.Core.Testing;

namespace Module.Extractor.Tests.Helpers;

public abstract class ExtractorTestBase
{
    protected TempDirectory Tmp { get; private set; } = null!;

    [TestInitialize]
    public void BaseSetup()
    {
        Tmp = new TempDirectory("ExtractorTests");
    }

    [TestCleanup]
    public void BaseCleanup() => Tmp.Dispose();
}
