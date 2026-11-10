using Kora.Core.Tools;

namespace Kora.Core.Maintenance;

public interface IApplicationMaintenanceMcp
{
    ValueTask<CapabilityReply> InvokeAsync(string id, string input, Func<bool> admitted, CancellationToken token);
}
