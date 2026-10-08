using Kora.Core.Authorization;
using Kora.Core.Communication;
using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Original-user control of the current-run manual layer, never audio, grant or question authority.</summary>
public interface IManualCallControlStore
{
    ValueTask<WorkSessionAuthorization> CreateManualCallControlSessionAsync(
        HostRequest request, Func<bool> admitted, CancellationToken cancellationToken);

    /// <summary>
    /// Holds the committed intent/session lease through required requested and outcome audits and
    /// the process-memory transition. A failed outcome receipt does not imply rollback.
    /// </summary>
    ValueTask<CallMutationOutcome> ApplyManualCallAsync(HostRequest request, HostRevision generation,
        bool active, Func<bool> admitted, Func<Task<CallMutationOutcome>> transition, CancellationToken cancellationToken);
}
