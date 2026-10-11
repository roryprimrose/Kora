using Kora.Core.Tools;
using Kora.Core.Hosting;

namespace Kora.Application.Hosting;

internal interface IDeterministicVersionQueueAction
{
    CapabilityReply Observe(SessionQueueReadProfile profile, CancellationToken token);
}
