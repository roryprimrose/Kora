using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    private ISessionRetentionControlStore RetentionControls => store as ISessionRetentionControlStore
        ?? throw new InvalidOperationException("Exact retention observations and controls are unavailable.");

    public Task<SessionRetentionObservation> ReadRetentionAsync(HostId<SessionIdentity> session,
        CancellationToken token) => ReadAsync(async () =>
    {
        var observed = await RetentionControls.ReadRetentionObservationAsync(session, token).ConfigureAwait(false);
        if (observed.State.SessionId != session)
        {
            throw new InvalidDataException("Retention observation does not belong to the exact selected session.");
        }
        return observed;
    }, token, retentionStatusOnly: true);

    public async Task<SessionRetentionObservation> SetRetentionHoldAsync(SessionRetentionObservation expected,
        bool perpetual, RequestOrigin origin, Func<bool> admission, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(admission);
        // Native review is mandatory. An existing model/system/stopped callback cannot relabel itself.
        if (origin != RequestOrigin.LocalUi || HostActivity.HasScope && HostActivity.Current is null
            || HostActivity.Current is { } current
            && (current.Request.Origin != RequestOrigin.LocalUi || current.Activity!.IsStopped
                || current.Request.InvocationId is not null || current.Request.SessionId != expected.State.SessionId))
        {
            throw new InvalidOperationException("Retention control requires fresh original native user input.");
        }
        if (expected.State.Purged || expected.Removed)
        {
            throw new InvalidOperationException("Purged session retention cannot be changed.");
        }
        using var observationActivity = HostActivity.BeginRoot(
            new(new(Guid.NewGuid()), expected.State.SessionId, new(Guid.NewGuid()), origin),
            HostActivityLayer.Application, HostOperation.Policy);
        try
        {
            var committed = await ControlAsync(expected.State.SessionId, origin,
                (request, eligible) => RetentionControls.SetRetentionHoldAsync(request, expected, perpetual, eligible, token),
                token, admission, existingSubject: true).ConfigureAwait(false);
            // Receipt/readback failures are uncertainty, not rollback. Never replay the setting automatically.
            var readback = await ReadRetentionAsync(expected.State.SessionId, token).ConfigureAwait(false);
            if (!admission() || readback.State != committed.State || readback.Generation != committed.Generation
                || readback.ExemptionAuditSequence != committed.ExemptionAuditSequence)
            {
                throw new InvalidOperationException("Retention committed but current readback or native admission changed. Refresh durable state before retrying.");
            }
            observationActivity.Complete(HostOperationOutcome.Completed);
            return readback;
        }
        catch (OperationCanceledException)
        {
            observationActivity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch
        {
            observationActivity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }
}
