using Module.Core.Pipeline;
using Module.Core.Testing;
using Module.Ps3Pipeline.Bridge;

namespace Module.Ps3Pipeline.Tests.Helpers;

public class FakePs3PipelineBridge : FakeBridge<PipelineStatusEvent>, IPs3PipelineBridge
{
    public IReadOnlyList<PipelineStatusEvent> StatusEvents => AllEvents;
}
