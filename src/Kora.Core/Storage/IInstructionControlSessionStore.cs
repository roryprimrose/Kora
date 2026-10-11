using Kora.Core.Authorization;
using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Durable original-local-input session fence for owned definition controls.</summary>
public interface IInstructionControlSessionStore
{
    /// <summary>Creates an instruction-management control session from admitted original input.</summary>
    ValueTask<WorkSessionAuthorization> CreateInstructionSession(
        HostRequest request, Func<bool> admitted, CancellationToken token);
    /// <summary>Commits a control operation only in the current instruction session generation.</summary>
    ValueTask<T> WithInstructionSession<T>(HostRequest request, HostRevision generation, Func<T> operation, CancellationToken token);
}
