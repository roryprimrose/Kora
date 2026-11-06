using Kora.Core.Auditing;
using Kora.Core.Hosting;
using Kora.Core.Skills;

namespace Kora.Application.Skills;

/// <summary>One original-input, expiring two-stage review of an exact owned source deletion.</summary>
public sealed class OwnedSkillDeletionWorkflow(
    IOwnedSkillDeletionStore store, OwnedSkillControlAdmission admission, ISecurityAuditLog audit, TimeProvider time)
{
    public async Task<bool> Delete(string id, string version, OwnedSkillAuthoringAuthority authority,
        Func<bool> exactInput, IOwnedSkillDeletionReview review, CancellationToken token)
    {
        var observed = false;
        try
        {
            return await admission.Run(authority, () => authority.IsCurrent() && exactInput(),
                async (request, session, current, admitted) =>
                {
                    var expires = time.GetUtcNow().AddMinutes(2);
                    bool Eligible() => current() && authority.IsCurrent() && exactInput() && time.GetUtcNow() < expires;
                    var source = await store.PrepareDeletion(id, version, admitted).ConfigureAwait(false);
                    if (!Eligible() || !await review.ReviewDeletion(source, admitted).ConfigureAwait(false)) { return false; }
                    admitted.ThrowIfCancellationRequested();
                    await store.ValidateDeletion(source, admitted).ConfigureAwait(false);
                    if (!Eligible() || !await review.ConfirmDeletion(source, admitted).ConfigureAwait(false)) { return false; }
                    await admission.Commit(request, session, Eligible, static () => true, admitted).ConfigureAwait(false);
                    var entry = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
                        "definition.owned-skill.delete", SecurityAuditOutcome.Requested,
                        request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
                        "definition.owned-skill");
                    audit.Write(entry);
                    try
                    {
                        await store.Delete(source, Eligible,
                            operation => admission.Commit(request, session, Eligible, operation, admitted).AsTask(), admitted).ConfigureAwait(false);
                        observed = true;
                        await admission.Commit(request, session, Eligible, static () => true, admitted).ConfigureAwait(false);
                        audit.Write(entry.WithOutcome(SecurityAuditOutcome.Succeeded));
                        return true;
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException)
                    {
                        var uncertain = observed || exception is OwnedSkillDeletionUnconfirmedException;
                        try
                        {
                            audit.Write(entry.WithOutcome(uncertain ? SecurityAuditOutcome.Unknown
                                : exception is OperationCanceledException ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Failed,
                                uncertain ? "deletion-unconfirmed" : "deletion-not-observed"));
                        }
                        catch (Exception auditFailure) when (uncertain && auditFailure is not OutOfMemoryException)
                        { throw new OwnedSkillDeletionUnconfirmedException(new AggregateException(exception, auditFailure)); }
                        if (observed && exception is not OwnedSkillDeletionUnconfirmedException)
                        { throw new OwnedSkillDeletionUnconfirmedException(exception); }
                        throw;
                    }
                }, token).ConfigureAwait(false);
        }
        catch (Exception exception) when (observed && exception is not (OwnedSkillDeletionUnconfirmedException or OutOfMemoryException))
        { throw new OwnedSkillDeletionUnconfirmedException(exception); }
    }
}
