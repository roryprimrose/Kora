using Kora.Core.Auditing;
using Kora.Core.Hosting;
using Kora.Core.Skills;

namespace Kora.Application.Skills;

public sealed class OwnedSkillRenameWorkflow(IOwnedDeclarativeSkillStore store, IOwnedSkillRenameStore renames,
    OwnedSkillControlAdmission admission, ISecurityAuditLog audit, TimeProvider time)
{
    public Task<IReadOnlyList<OwnedSkillRename>> Pending(OwnedSkillAuthoringAuthority authority, CancellationToken token) =>
        admission.Run(authority, authority.IsCurrent, (_, _, _, admitted) => renames.PendingRenames(admitted), token);

    public async Task<OwnedDeclarativeSkillSnapshot?> Rename(string id, string version, string? newId, bool recovery,
        OwnedSkillAuthoringAuthority authority, Func<bool> exactInput, IOwnedSkillRenameReview review, CancellationToken token)
    {
        var observed = false;
        try
        {
            return await admission.Run<OwnedDeclarativeSkillSnapshot?>(authority, authority.IsCurrent,
                async (request, session, current, admitted) =>
                {
                    OwnedDeclarativeSkillDocument.RequireIdentity(id, version);
                    var proposal = recovery
                        ? (await renames.PendingRenames(admitted).ConfigureAwait(false)).SingleOrDefault(p =>
                            string.Equals(p.Original.Document.Id, id, StringComparison.Ordinal)
                            && string.Equals(p.Original.Document.Version, version, StringComparison.Ordinal))
                            ?? throw new InvalidOperationException("No retained rename matches that exact original ID/version. Inspect both names; no retry is authorized.")
                        : new OwnedSkillRename(await store.Read(id, version, admitted).ConfigureAwait(false)
                            ?? throw new InvalidDataException("The exact source package is absent."), newId
                            ?? throw new InvalidDataException("A new canonical ID is required."));
                    var started = time.GetTimestamp();
                    bool Eligible() => current() && authority.IsCurrent() && exactInput()
                        && time.GetElapsedTime(started) < TimeSpan.FromMinutes(2);
                    var source = store.Destination(proposal.Original.Document);
                    var destination = store.Destination(proposal.Renamed);
                    if (!Eligible() || !await review.ReviewRename(proposal, source, destination, recovery, admitted).ConfigureAwait(false))
                    { return null; }
                    admitted.ThrowIfCancellationRequested();
                    if (!Eligible() || !await review.ConfirmRename(proposal, source, destination, recovery, admitted).ConfigureAwait(false))
                    { return null; }
                    await admission.Commit(request, session, Eligible, static () => true, admitted).ConfigureAwait(false);
                    var entry = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
                        recovery ? "definition.owned-skill.recover-rename" : "definition.owned-skill.rename", SecurityAuditOutcome.Requested,
                        request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
                        "definition.owned-skill");
                    audit.Write(entry);
                    try
                    {
                        var result = await renames.Rename(proposal, recovery, Eligible,
                            operation => admission.Commit(request, session, Eligible, operation, admitted).AsTask(), admitted).ConfigureAwait(false);
                        observed = true;
                        await admission.Commit(request, session, Eligible, static () => true, admitted).ConfigureAwait(false);
                        audit.Write(entry.WithOutcome(SecurityAuditOutcome.Succeeded));
                        return result;
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException)
                    {
                        audit.Write(entry.WithOutcome(observed || exception is OwnedSkillRenameUnconfirmedException ? SecurityAuditOutcome.Unknown
                            : exception is OperationCanceledException ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Failed,
                            observed || exception is OwnedSkillRenameUnconfirmedException ? "rename-unconfirmed" : "rename-not-observed"));
                        throw;
                    }
                }, token).ConfigureAwait(false);
        }
        catch (Exception exception) when (observed && exception is not (OwnedSkillRenameUnconfirmedException or OutOfMemoryException))
        { throw new OwnedSkillRenameUnconfirmedException(OwnedSkillRenameEffect.NativeReadbackVerified, exception); }
    }
}
