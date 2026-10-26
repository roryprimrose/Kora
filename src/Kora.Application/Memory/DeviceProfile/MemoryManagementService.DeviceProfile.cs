using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Memory;

namespace Kora.Application.Memory;

internal sealed partial class MemoryManagementService
{
    private readonly MemoryAdmissionService profileAdmission;
    private readonly SessionMemoryContextSource profileContextSource;

    internal Task<MemoryCommandResult> ExecuteDeviceProfileAsync(MemoryCommand command, RequestOrigin origin,
        Func<bool> admitted, CancellationToken token) =>
        ExecuteCoreAsync(command, origin, admitted, MemoryScopeKind.DeviceProfile, token);

    internal SessionMemoryContext CreateDeviceProfileContextWorkflow()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return new(profileContextSource, access.Underlying, contextTime, MemoryScopeKind.DeviceProfile);
    }
}
