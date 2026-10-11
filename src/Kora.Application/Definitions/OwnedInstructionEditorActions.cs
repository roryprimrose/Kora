using Kora.Core.Artifacts;
using Kora.Core.Auditing;
using Kora.Core.Hosting;
using Kora.Core.Commands;

namespace Kora.Application.Definitions;

public sealed class OwnedInstructionEditorActions(
    IOwnedInstructionStore store, InstructionControlAdmission admission,
    InstructionRevisionCatalogue revisions, ArtifactCatalogue catalogue, ISecurityAuditLog auditLog,
    BuiltInCommandRouter controls)
{
    public Task<IReadOnlyList<string>> List(Func<bool> eligible, CancellationToken token) =>
        admission.Run(eligible, async (_, _, _, admitted) => await store.List(admitted).ConfigureAwait(false), token);

    public Task<OwnedInstructionSnapshot?> Inspect(string name, Func<bool> eligible, CancellationToken token) =>
        admission.Run(eligible, async (_, _, _, admitted) => await store.Read(name, admitted).ConfigureAwait(false), token);

    public ArtifactFileDocument Validate(string name, string text, bool bom)
    {
        OwnedInstructionNames.WriteName(name);
        var document = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(text, bom),
            ArtifactKind.Instruction, "kora-profile", name);
        document.RequireEditableInstruction();
        var proposed = document.Definition;
        if (proposed.CommandName is "skill" or "skills" or "instruction" or "instructions" or "prompt"
            or "prompts" or "agent" or "agents" or "definitions" or "help" or "exit" or "settings")
        { throw new InvalidDataException("The instruction cannot shadow a reserved control."); }
        if (proposed.SpokenNames.Any(name => new[] { "use " + name, "run " + name }.Any(phrase => controls.Match(phrase).IsMatch)))
        { throw new InvalidDataException("The declared instruction name would shadow a registered built-in command."); }
        _ = new ArtifactCatalogue([.. catalogue.Candidates.Where(item => !string.Equals(item.OwnedInstructionName, name, StringComparison.Ordinal)), proposed]);
        return document;
    }

    public Task<InstructionWriteResult> Save(string name, OwnedInstructionSnapshot? expected,
        ArtifactFileDocument? replacement, Func<CancellationToken, Task<bool>> exactConfirmation,
        Func<bool> eligible, CancellationToken token) =>
        admission.Run(eligible, async (request, session, current, admitted) =>
        {
            if (!await exactConfirmation(admitted).ConfigureAwait(false))
            { return new InstructionWriteResult(false, false, false, expected); }
            admitted.ThrowIfCancellationRequested();
            await admission.Publish(request, session, current, static () => true, admitted).ConfigureAwait(false);
            var audit = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
                replacement is null ? "definition.instruction.delete" : "definition.instruction.save",
                SecurityAuditOutcome.Requested, request.Origin == RequestOrigin.ActivatedVoice
                    ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser, "definition.owned-instruction");
            auditLog.Write(audit);
            var unchanged = expected is not null && replacement is not null
                && string.Equals(expected.Document.Definition.Digest, replacement.Definition.Digest, StringComparison.Ordinal);
            var publicationObserved = false;
            // Hold before any physical publication; failed/uncertain saves never leave old guidance current.
            try
            {
                if (!unchanged) { HoldRevision(name); }
                var result = await store.Write(name, expected, replacement, current,
                    operation => admission.Publish(request, session, current, operation, admitted).AsTask(), admitted).ConfigureAwait(false);
                publicationObserved = result.Published;
                await admission.Publish(request, session, current, static () => true, admitted).ConfigureAwait(false);
                if (result.Conflict) { HoldRevision(name); }
                auditLog.Write(audit.WithOutcome(result.Conflict ? SecurityAuditOutcome.Failed : SecurityAuditOutcome.Succeeded,
                    result.Conflict ? "base-conflict" : null));
                return result;
            }
            catch (Exception exception)
            {
                catalogue.Publish(catalogue.Candidates.Select(item => string.Equals(item.OwnedInstructionName, name, StringComparison.Ordinal)
                    ? item with { IsUseEnabled = false } : item).ToArray());
                var observed = publicationObserved || exception is InstructionPublicationUnconfirmedException;
                auditLog.Write(audit.WithOutcome(observed ? SecurityAuditOutcome.Unknown
                    : exception is OperationCanceledException ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Failed,
                    observed ? "publication-observed-unconfirmed" : "publication-not-confirmed"));
                if (unchanged) { revisions.Hold(name); }
                if (publicationObserved && exception is not InstructionPublicationUnconfirmedException)
                { throw new InstructionPublicationUnconfirmedException(admitted.IsCancellationRequested, exception); }
                throw;
            }
        }, token);

    private void HoldRevision(string name)
    {
        catalogue.Publish(catalogue.Candidates.Select(item => string.Equals(item.OwnedInstructionName, name, StringComparison.Ordinal)
            ? item with { IsUseEnabled = false } : item).ToArray());
        revisions.Hold(name);
    }

    public async Task<bool> Activate(OwnedInstructionSnapshot reviewed,
        Func<CancellationToken, Task<bool>> exactConfirmation, Func<bool> eligible, CancellationToken token)
    {
        reviewed.Document.RequireEditableInstruction();
        try
        {
        var outcome = await admission.Run(eligible, async (request, session, current, admitted) =>
        {
            if (!await exactConfirmation(admitted).ConfigureAwait(false)) { return false; }
            var live = await store.Read(reviewed.Name, admitted).ConfigureAwait(false);
            if (!current() || live is null || !string.Equals(live.DirectoryIdentity, reviewed.DirectoryIdentity, StringComparison.Ordinal)
                || !string.Equals(live.FileIdentity, reviewed.FileIdentity, StringComparison.Ordinal)
                || !string.Equals(live.Document.Definition.Digest, reviewed.Document.Definition.Digest, StringComparison.Ordinal))
            { throw new InvalidOperationException("The reviewed instruction revision changed. Reload and review again."); }
            var audit = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
                "definition.instruction.review-use", SecurityAuditOutcome.Requested, request.Origin == RequestOrigin.ActivatedVoice
                    ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
                "definition.owned-instruction");
            auditLog.Write(audit);
            try
            {
                await admission.Publish(request, session, current, () =>
                {
                    revisions.PrepareActivation(live);
                    return true;
                }, admitted).ConfigureAwait(false);
                var candidate = catalogue.Candidates.SingleOrDefault(item => string.Equals(item.OwnedInstructionName, live.Name, StringComparison.Ordinal));
                if (candidate is null || !string.Equals(candidate.Digest, live.Document.Definition.Digest, StringComparison.Ordinal)
                    || !string.Equals(candidate.OwnedInstructionRevision, live.Revision, StringComparison.Ordinal))
                { throw new InvalidOperationException("The reviewed revision is not the coherent current catalogue candidate."); }
                auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded, "exact-revision-reviewed"));
                return true;
            }
            catch
            {
                auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Failed, "activation-unconfirmed"));
                revisions.Hold(reviewed.Name);
                throw;
            }
        }, token).ConfigureAwait(false);
        if (outcome)
        {
            if (!eligible() || token.IsCancellationRequested)
            { throw new InvalidOperationException("Reviewed instruction use expired before publication."); }
            revisions.CompleteActivation(reviewed);
            catalogue.Publish(catalogue.Candidates.Select(item => string.Equals(item.OwnedInstructionName, reviewed.Name, StringComparison.Ordinal)
                && string.Equals(item.OwnedInstructionRevision, reviewed.Revision, StringComparison.Ordinal)
                    ? item with { IsUseEnabled = true } : item).ToArray());
        }
        return outcome;
        }
        catch
        {
            catalogue.Publish(catalogue.Candidates.Select(item => string.Equals(item.OwnedInstructionName, reviewed.Name, StringComparison.Ordinal)
                ? item with { IsUseEnabled = false } : item).ToArray());
            revisions.Hold(reviewed.Name);
            throw;
        }
    }

    public async Task<bool> IsCurrent(ArtifactDefinition candidate, Func<bool> eligible, CancellationToken token)
    {
        if (candidate.OwnedInstructionName is null) { return true; }
        OwnedInstructionSnapshot? live;
        try
        {
            live = await Inspect(candidate.OwnedInstructionName, eligible, token).ConfigureAwait(false);
            if (live is not null && string.Equals(candidate.OwnedInstructionRevision, live.Revision, StringComparison.Ordinal)
                && catalogue.IsCurrent(candidate.Id, candidate.Digest) && revisions.IsEnabled(live))
            { return true; }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidDataException or InvalidOperationException or OperationCanceledException)
        {
            HoldRevision(candidate.OwnedInstructionName);
            return false;
        }
        HoldRevision(candidate.OwnedInstructionName);
        return false;
    }

    public void SuspendReviewedUse()
    {
        var names = revisions.SuspendReviewedUse();
        catalogue.Publish(catalogue.Candidates.Select(item => item.OwnedInstructionName is { } name
            && names.Contains(name, StringComparer.Ordinal) ? item with { IsUseEnabled = false } : item).ToArray());
    }
}
