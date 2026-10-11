using Kora.Application.Tools;
using Kora.Core.Tools;
using Kora.Core.Hosting;

namespace Kora.Application.Hosting;

internal sealed class DeterministicVersionQueueAction(ReadOnlyCapabilityRegistry registry) : IDeterministicVersionQueueAction
{
    public CapabilityReply Observe(SessionQueueReadProfile profile, CancellationToken token) =>
        registry.Invoke(registry.Admit(CapabilityLane.Native), SessionQueueReadProfiles.CapabilityId(profile), "{}", token);
}
