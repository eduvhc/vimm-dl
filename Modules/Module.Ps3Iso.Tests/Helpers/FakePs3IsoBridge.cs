using Module.Core.Testing;
using Module.Ps3Iso.Bridge;

namespace Module.Ps3Iso.Tests.Helpers;

public class FakePs3IsoBridge : FakeBridge<Ps3IsoEvent>, IPs3IsoBridge
{
    public IReadOnlyList<Ps3IsoStatusEvent> StatusEvents => Of<Ps3IsoStatusEvent>();
    public Ps3IsoStatusEvent? LastStatus => Last<Ps3IsoStatusEvent>();
}
