using Kora.Core.Tools;

namespace Kora.Application.Hosting;

internal interface IDeterministicVersionQueueAction
{
    CapabilityReply Observe(CancellationToken token);
}
