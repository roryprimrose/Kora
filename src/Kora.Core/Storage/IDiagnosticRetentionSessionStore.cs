using Kora.Core.Authorization;
using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Host-only diagnostic preference admission, never an audio session, grant or execution token.</summary>
public interface IDiagnosticRetentionSessionStore
{
    ValueTask<WorkSessionAuthorization> CreateDiagnosticRetentionSessionAsync(
        HostRequest request, Func<bool> admitted, CancellationToken cancellationToken);

    ValueTask<T> WithDiagnosticRetentionSessionAsync<T>(HostRequest request, HostRevision generation,
        Func<T> operation, CancellationToken cancellationToken);
}
