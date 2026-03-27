using Microsoft.Extensions.Logging.Abstractions;
using Module.Core.Testing;

namespace Module.Ps3Pipeline.Tests.Helpers;

public abstract class Ps3PipelineTestBase
{
    protected FakePs3PipelineBridge Bridge { get; private set; } = null!;
    protected TempDirectory Tmp { get; private set; } = null!;

    [TestInitialize]
    public void BaseSetup()
    {
        Tmp = new TempDirectory("Ps3PipelineTests");
        Bridge = new FakePs3PipelineBridge();
    }

    [TestCleanup]
    public void BaseCleanup() => Tmp.Dispose();

    protected Ps3ConversionPipeline CreatePipeline()
        => new(Bridge, NullLogger<Ps3ConversionPipeline>.Instance);
}
