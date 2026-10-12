using Kora.Application.Tools;
using Kora.Core.Tools;
using Kora.Core.Hosting;

namespace Kora.Application.Hosting;

internal sealed class DeterministicVersionQueueAction(ReadOnlyCapabilityRegistry registry) : IDeterministicVersionQueueAction
{
    public CapabilityReply Observe(SessionQueueReadProfile profile, CancellationToken token, SessionQueueCapabilityTarget? target = null)
    {
        var input = SessionQueueReadProfiles.Input(profile, target);
        return registry.Invoke(registry.Admit(CapabilityLane.Native), SessionQueueReadProfiles.CapabilityId(profile), input, token);
    }
}
