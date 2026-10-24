using Kora.Core.Auditing;
using Kora.Core.Hosting;
using Kora.Core.Skills;

namespace Kora.Application.Skills;

public sealed class OwnedSkillSaveWorkflow(
    IOwnedDeclarativeSkillStore store, OwnedSkillControlAdmission admission, ISecurityAuditLog audit)
{
    public Task<IReadOnlyList<OwnedDeclarativeSkillSnapshot>> List(
        OwnedSkillAuthoringAuthority authority, CancellationToken token) =>
        admission.Run(authority, authority.IsCurrent,
            (_, _, _, admitted) => store.List(admitted), token);

    public Task<OwnedDeclarativeSkillSnapshot?> Read(string id, string version,
        OwnedSkillAuthoringAuthority authority, CancellationToken token) =>
        admission.Run(authority, authority.IsCurrent,
            (_, _, _, admitted) => store.Read(id, version, admitted), token);

    public async Task<OwnedDeclarativeSkillSnapshot?> Save(OwnedDeclarativeSkillDocument document,
        OwnedSkillAuthoringAuthority authority, Func<bool> exactDraft, IOwnedSkillSaveReview review, CancellationToken token)
    {
        var observed = false;
        try
        {
        return await admission.Run<OwnedDeclarativeSkillSnapshot?>(authority, authority.IsCurrent,
            async (request, session, current, admitted) =>
            {
                bool Eligible() => current() && authority.IsCurrent() && exactDraft();
                var destination = store.Destination(document);
                if (!Eligible() || !await review.Review(document, destination, admitted).ConfigureAwait(false))
                { return null; }
                admitted.ThrowIfCancellationRequested();
                if (!Eligible() || !await review.ConfirmPublication(document, destination, admitted).ConfigureAwait(false))
                { return null; }
                await admission.Commit(request, session, Eligible, static () => true, admitted).ConfigureAwait(false);
                var entry = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
                    "definition.owned-skill.create", SecurityAuditOutcome.Requested,
                    request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
                    "definition.owned-skill");
                audit.Write(entry);
                var published = false;
                try
                {
                    var result = await store.Publish(document, Eligible, operation =>
                        admission.Commit(request, session, Eligible, operation, admitted).AsTask(), admitted).ConfigureAwait(false);
                    published = true;
                    observed = true;
                    await admission.Commit(request, session, Eligible, static () => true, admitted).ConfigureAwait(false);
                    audit.Write(entry.WithOutcome(SecurityAuditOutcome.Succeeded));
                    return result;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    var uncertain = published || exception is OwnedSkillPublicationUnconfirmedException;
                    audit.Write(entry.WithOutcome(uncertain ? SecurityAuditOutcome.Unknown
                        : exception is OperationCanceledException ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Failed,
                        uncertain ? "publication-unconfirmed" : "publication-not-observed"));
                    if (published && exception is not OwnedSkillPublicationUnconfirmedException)
                    { throw new OwnedSkillPublicationUnconfirmedException(exception); }
                    throw;
                }
            }, token).ConfigureAwait(false);
        }
        catch (Exception exception) when (observed && exception is not (OwnedSkillPublicationUnconfirmedException or OutOfMemoryException))
        { throw new OwnedSkillPublicationUnconfirmedException(exception); }
    }
}
