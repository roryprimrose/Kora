using Kora.Application.Tools;
using Kora.Core.Tools;

namespace Kora.Application.Hosting;

internal sealed class DeterministicVersionQueueAction(ReadOnlyCapabilityRegistry registry) : IDeterministicVersionQueueAction
{
    public CapabilityReply Observe(CancellationToken token) =>
        registry.Invoke(registry.Admit(CapabilityLane.Native), ReadOnlyCapabilityCatalog.Version, "{}", token);
}
