using Kora.Core.Auditing;
using Kora.Core.Customizations;
using Kora.Core.Hosting;

namespace Kora.Application.Definitions;

public sealed partial class CustomizationManagementService
{
    public async Task<CustomizationDeletionOutcome> Delete(CustomizationSnapshot reviewed,
        Func<CancellationToken, Task<bool>> confirm, Func<bool> eligible, CancellationToken token)
    {
        CustomizationDeletion.Validate(reviewed);
        var deleted = false;
        try
        {
            return await admission.Run(eligible, async (request, session, current, admitted) =>
            {
                var controlRevision = use.Revision;
                var observed = catalogue.SourceCandidates;
                var reloadFailure = catalogue.ReloadFailureRevision;
                if (!await confirm(admitted).ConfigureAwait(false)) { return CustomizationDeletionOutcome.Cancelled; }
                await RequireUnchangedSourceCore(reviewed.Source, reviewed, current, admitted).ConfigureAwait(false);
                if (!ReferenceEquals(observed, catalogue.SourceCandidates) || catalogue.ReloadFailureRevision != reloadFailure
                    || catalogue.IsReloadUnavailable)
                { throw new InvalidOperationException("The source catalogue changed or is unavailable. Reload and begin a fresh deletion review."); }
                var entry = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ResourceWrite,
                    "definition.source.delete", SecurityAuditOutcome.Requested,
                    request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
                    reviewed.Source.Id);
                audit.Write(entry);
                try
                {
                    await admission.Publish(request, session, current, () =>
                    {
                        if (use.Revision != controlRevision)
                        { throw new InvalidOperationException("Use control changed during deletion review. Begin a fresh exact review."); }
                        use.SetDisabled(reviewed.Source.Id, true);
                        return true;
                    }, admitted).ConfigureAwait(false);
                    await store.Delete(reviewed, current, async operation =>
                    {
                        await admission.Publish(request, session, current, () =>
                        {
                            operation();
                            deleted = true;
                            return true;
                        }, admitted).ConfigureAwait(false);
                    }, admitted).ConfigureAwait(false);
                    await admission.Publish(request, session, current, static () => true, admitted).ConfigureAwait(false);
                    audit.Write(entry.WithOutcome(SecurityAuditOutcome.Succeeded, "exact-source-deleted"));
                    return CustomizationDeletionOutcome.Deleted;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    var effected = deleted || exception is CustomizationDeletionUnconfirmedException;
                    try
                    {
                        audit.Write(entry.WithOutcome(effected ? SecurityAuditOutcome.Unknown
                            : exception is OperationCanceledException ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Failed,
                            effected ? "deletion-requires-inspection" : "source-not-deleted"));
                    }
                    catch (Exception failure) when (failure is not OutOfMemoryException)
                    { exception = new AggregateException(exception, failure); }
                    if (effected) { throw new CustomizationDeletionUnconfirmedException(exception); }
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
                    throw;
                }
            }, token).ConfigureAwait(false);
        }
        catch (Exception exception) when (deleted && exception is not CustomizationDeletionUnconfirmedException
            && exception is not OutOfMemoryException)
        { throw new CustomizationDeletionUnconfirmedException(exception); }
    }
}
