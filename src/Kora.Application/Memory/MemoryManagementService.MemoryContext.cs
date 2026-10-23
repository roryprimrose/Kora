using Kora.Application.Hosting;

namespace Kora.Application.Memory;

internal sealed partial class MemoryManagementService
{
    private readonly SessionMemoryContextSource contextSource;
    private readonly TimeProvider contextTime;

    internal SessionMemoryContext CreateContextWorkflow()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return new(contextSource, access.Underlying, contextTime);
    }
}
