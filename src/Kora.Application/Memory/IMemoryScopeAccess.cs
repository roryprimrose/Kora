using Kora.Core.Hosting;
using Kora.Core.Memory;

namespace Kora.Application.Memory;

// The dependent composition must resolve profile/source identities from host state, not model fields.
internal interface IMemoryScopeAccess
{
    MemoryBoundary? Observe(HostRequest request);
}
