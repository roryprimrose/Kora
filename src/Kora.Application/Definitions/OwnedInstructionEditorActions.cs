using Kora.Core.Artifacts;
using Kora.Core.Auditing;
using Kora.Core.Hosting;
using Kora.Core.Commands;

namespace Kora.Application.Definitions;

public sealed class OwnedInstructionEditorActions(
    IOwnedInstructionStore store, InstructionControlAdmission admission,
    InstructionRevisionCatalogue revisions, ArtifactCatalogue catalogue, ISecurityAuditLog auditLog,
    BuiltInCommandRouter controls, ArtifactKind kind = ArtifactKind.Instruction)
{
    public ArtifactKind Kind { get; } = ValidateKind(kind);

    private static ArtifactKind ValidateKind(ArtifactKind value)
    {
        _ = OwnedDefinitionKind.DirectoryName(value);
        return value;
    }

    private bool Matches(ArtifactDefinition candidate, string name) =>
        candidate.Kind == Kind && string.Equals(candidate.OwnedInstructionName, name, StringComparison.Ordinal);

    public Task<IReadOnlyList<string>> List(Func<bool> eligible, CancellationToken token) =>
        admission.Run(eligible, async (_, _, _, admitted) => await store.List(admitted).ConfigureAwait(false), token);

    public Task<OwnedInstructionSnapshot?> Inspect(string name, Func<bool> eligible, CancellationToken token) =>
        admission.Run(eligible, async (_, _, _, admitted) => await store.Read(name, admitted).ConfigureAwait(false), token);

    public ArtifactFileDocument Validate(string name, string text, bool bom)
    {
        OwnedInstructionNames.WriteName(name);
        var document = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(text, bom),
            Kind, "kora-profile", name);
        document.RequireEditableInstruction();
        var proposed = document.Definition;
        if (proposed.CommandName is "skill" or "skills" or "instruction" or "instructions" or "prompt"
            or "prompts" or "agent" or "agents" or "definitions" or "help" or "exit" or "settings")
        { throw new InvalidDataException("The instruction cannot shadow a reserved control."); }
        if (proposed.SpokenNames.Any(name => new[] { "use " + name, "run " + name }.Any(phrase => controls.Match(phrase).IsMatch)))
        { throw new InvalidDataException("The declared instruction name would shadow a registered built-in command."); }
        _ = new ArtifactCatalogue([.. catalogue.Candidates.Where(item => !Matches(item, name)), proposed]);
        return document;
    }

    public OwnedDefinitionCopy PrepareCopy(string destinationName, OwnedInstructionSnapshot source, string draft)
    {
        source.Document.RequireOwnedCopySource();
        OwnedInstructionNames.WriteName(source.Name);
        OwnedInstructionNames.WriteName(destinationName);
        if (source.Document.Definition.Kind != Kind
            || !string.Equals(source.CanonicalFileName, source.Name + OwnedDefinitionKind.Extension(Kind), StringComparison.Ordinal)
            || string.Equals(source.Name, destinationName, StringComparison.Ordinal))
        { throw new InvalidDataException("A copy requires a supported canonical original owned source and a different new same-kind owned ID."); }
        // No legacy upgrade or source re-read: both base and draft are the user's frozen owned editor snapshots.
        var frozenDraft = ArtifactFileDocument.Parse(ArtifactFileDocument.Encode(draft, source.Document.HasBom), Kind, "kora-profile", source.Name);
        var proposed = frozenDraft.WithOwnedCopyName(destinationName);
        proposed = Validate(destinationName, proposed.Text, proposed.HasBom);
        return new(source, frozenDraft, destinationName, proposed);
    }

    public Task<InstructionWriteResult> Save(string name, OwnedInstructionSnapshot? expected,
        ArtifactFileDocument? replacement, Func<CancellationToken, Task<bool>> exactConfirmation,
        Func<bool> eligible, CancellationToken token, OwnedDefinitionCopy? copy = null,
        Func<OwnedInstructionSnapshot, bool>? reconcile = null) =>
        admission.Run(eligible, async (request, session, current, admitted) =>
        {
            if (expected is not null && expected.Document.Definition.Kind != Kind
                || replacement is not null && replacement.Definition.Kind != Kind)
            { throw new InvalidDataException("The exact mutation belongs to a different owned kind."); }
            if (copy is not null && (expected is not null || !ReferenceEquals(replacement, copy.Proposed)
                || !string.Equals(name, copy.DestinationName, StringComparison.Ordinal)))
            { throw new InvalidDataException("The copy approval does not match the exact new-file proposal."); }
            if (!await exactConfirmation(admitted).ConfigureAwait(false))
            { return new InstructionWriteResult(false, false, false, expected); }
            admitted.ThrowIfCancellationRequested();
            await admission.Publish(request, session, current, static () => true, admitted).ConfigureAwait(false);
            var audit = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
                copy is not null ? Kind == ArtifactKind.Instruction ? "definition.instruction.copy" : "definition.prompt.copy"
                    : replacement is null ? Kind == ArtifactKind.Instruction ? "definition.instruction.delete" : "definition.prompt.delete"
                    : Kind == ArtifactKind.Instruction ? "definition.instruction.save" : "definition.prompt.save",
                SecurityAuditOutcome.Requested, request.Origin == RequestOrigin.ActivatedVoice
                    ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
                copy?.AuditTarget ?? (Kind == ArtifactKind.Instruction ? "definition.owned-instruction" : "definition.owned-prompt"));
            auditLog.Write(audit);
            var unchanged = expected is not null && replacement is not null
                && string.Equals(expected.Document.Definition.Digest, replacement.Definition.Digest, StringComparison.Ordinal);
            var publicationObserved = false;
            // Hold before any physical publication; failed/uncertain saves never leave old guidance current.
            try
            {
                if (copy is not null) { _ = Validate(name, copy.Proposed.Text, copy.Proposed.HasBom); }
                if (!unchanged) { HoldRevision(name, existingOnly: copy is not null); }
                var result = await store.Write(name, expected, replacement, current,
                    operation => admission.Publish(request, session, current, operation, admitted).AsTask(), admitted).ConfigureAwait(false);
                publicationObserved = result.Published;
                await admission.Publish(request, session, current, static () => true, admitted).ConfigureAwait(false);
                if (copy is not null && result.Published && reconcile is not null
                    && !await admission.Publish(request, session, current, () => reconcile(result.Current!), admitted).ConfigureAwait(false))
                { throw new IOException("The saved copy catalogue is unconfirmed. Inspect the destination; the original draft is retained."); }
                if (result.Conflict) { HoldRevision(name, existingOnly: copy is not null); }
                auditLog.Write(audit.WithOutcome(result.Conflict ? SecurityAuditOutcome.Failed : SecurityAuditOutcome.Succeeded,
                    result.Conflict ? "base-conflict" : null));
                return result;
            }
            catch (Exception exception)
            {
                catalogue.Publish(catalogue.Candidates.Select(item => Matches(item, name)
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

    private void HoldRevision(string name, bool existingOnly = false)
    {
        catalogue.Publish(catalogue.Candidates.Select(item => Matches(item, name)
            ? item with { IsUseEnabled = false } : item).ToArray());
        revisions.Hold(name, existingOnly);
    }

    public async Task<bool> Activate(OwnedInstructionSnapshot reviewed,
        Func<CancellationToken, Task<bool>> exactConfirmation, Func<bool> eligible, CancellationToken token)
    {
        reviewed.Document.RequireEditableInstruction();
        if (reviewed.Document.Definition.Kind != Kind)
        { throw new InvalidDataException("The reviewed definition belongs to another owned kind."); }
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
                Kind == ArtifactKind.Instruction ? "definition.instruction.review-use" : "definition.prompt.review-use",
                SecurityAuditOutcome.Requested, request.Origin == RequestOrigin.ActivatedVoice
                    ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
                Kind == ArtifactKind.Instruction ? "definition.owned-instruction" : "definition.owned-prompt");
            auditLog.Write(audit);
            try
            {
                await admission.Publish(request, session, current, () =>
                {
                    revisions.PrepareActivation(live);
                    return true;
                }, admitted).ConfigureAwait(false);
                var candidate = catalogue.Candidates.SingleOrDefault(item => Matches(item, live.Name));
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
            catalogue.Publish(catalogue.Candidates.Select(item => Matches(item, reviewed.Name)
                && string.Equals(item.OwnedInstructionRevision, reviewed.Revision, StringComparison.Ordinal)
                    ? item with { IsUseEnabled = true } : item).ToArray());
        }
        return outcome;
        }
        catch
        {
            catalogue.Publish(catalogue.Candidates.Select(item => Matches(item, reviewed.Name)
                ? item with { IsUseEnabled = false } : item).ToArray());
            revisions.Hold(reviewed.Name);
            throw;
        }
    }

    public async Task<bool> IsCurrent(ArtifactDefinition candidate, Func<bool> eligible, CancellationToken token)
    {
        if (candidate.OwnedInstructionName is null) { return true; }
        if (candidate.Kind != Kind) { return false; }
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
        catalogue.Publish(catalogue.Candidates.Select(item => item.Kind == Kind && item.OwnedInstructionName is { } name
            && names.Contains(name, StringComparer.Ordinal) ? item with { IsUseEnabled = false } : item).ToArray());
    }
}
