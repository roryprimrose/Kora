using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Memory;

namespace Kora.Application.Memory;

internal sealed partial class MemoryManagementService
{
    async Task<MemoryRecord> IMemoryBatchDisableActions.InspectAsync(HostId<SessionIdentity> session,
        MemorySummary selection, Func<bool> admitted, CancellationToken token)
    {
        var result = await ExecuteAsync(new(MemoryCommandOperation.Inspect, session.Value, selection.Id,
            selection.Revision, false, null), RequestOrigin.LocalUi, admitted, token).ConfigureAwait(false);
        if (result.Outcome is "RevisionConflict" or "NotFound")
        {
            throw new MemoryBatchDisableConflictException();
        }
        // ExecuteAsync's exact Inspect arm always supplies the record; missing/stale arms throw above.
        return result.Inspected!;
    }

    async Task IMemoryBatchDisableActions.DisableAsync(HostId<SessionIdentity> session, MemoryRecord record,
        Func<bool> admitted, Action<MemoryResult> completed, CancellationToken token)
    {
        await ExecuteAsync(new(MemoryCommandOperation.Disable, session.Value, record.Id.Value,
            record.Revision.Value, false, null), RequestOrigin.LocalUi, admitted, token, record, completed)
            .ConfigureAwait(false);
    }
}
