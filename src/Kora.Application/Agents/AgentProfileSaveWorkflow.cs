using Kora.Application.Definitions;
using Kora.Core.Agents;
using Kora.Core.Artifacts;
using Kora.Core.Auditing;
using Kora.Core.Hosting;

namespace Kora.Application.Agents;

public sealed class AgentProfileSaveWorkflow(IOwnedAgentProfileStore store,
    InstructionControlAdmission admission, ISecurityAuditLog audit)
{
    public OwnedAgentProfileDocument Validate(string name, string source) => OwnedAgentProfileDocument.Parse(name, source);

    public Task<OwnedAgentProfileDestination> PrepareNewAsync(string name, Func<bool> eligible, CancellationToken token) =>
        admission.Run(eligible, async (_, _, current, admitted) =>
        {
            var destination = await store.PrepareNew(name, admitted).ConfigureAwait(false);
            Require(current, admitted);
            return destination;
        }, token);

    public Task<IReadOnlyList<string>> ListAsync(Func<bool> eligible, CancellationToken token) =>
        admission.Run(eligible, async (_, _, current, admitted) =>
        {
            var result = await store.List(admitted).ConfigureAwait(false);
            Require(current, admitted);
            return result;
        }, token);

    public Task<OwnedAgentProfileSnapshot?> ReadAsync(string name, Func<bool> eligible, CancellationToken token) =>
        admission.Run(eligible, async (_, _, current, admitted) =>
        {
            var result = await store.Read(name, admitted).ConfigureAwait(false);
            Require(current, admitted);
            return result;
        }, token);

    public Task<AgentProfileSaveReview> ReviewAsync(string name, string source, long revision,
        RequestOrigin origin, Func<bool> eligible, CancellationToken token)
    {
        if (origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
        { throw new InvalidOperationException("Authoring requires original local user input."); }
        var document = Validate(name, source);
        return admission.Run(eligible, async (_, _, current, admitted) =>
        {
            var destination = await store.PrepareNew(name, admitted).ConfigureAwait(false);
            Require(current, admitted);
            return new AgentProfileSaveReview(this, destination, document, revision, origin, eligible);
        }, token);
    }

    public Task<AgentProfileSaveReview> ReviewUpdateAsync(OwnedAgentProfileSnapshot original, string source, long revision,
        RequestOrigin origin, Func<bool> eligible, CancellationToken token)
    {
        if (origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
        { throw new InvalidOperationException("Editing requires original local user input."); }
        var document = OwnedAgentProfileDocument.Parse(original.Destination.Name,
            ArtifactFileDocument.Encode(source, original.Document.Document.HasBom));
        return admission.Run(eligible, (_, _, current, admitted) =>
        {
            Require(current, admitted);
            return Task.FromResult(new AgentProfileSaveReview(this, original.Destination, document, revision, origin, eligible, original));
        }, token);
    }

    public Task<AgentProfileWriteResult> SaveAsNewAsync(AgentProfileSaveReview review,
        RequestOrigin confirmationOrigin, Func<CancellationToken, Task<bool>> nativeConfirmation,
        Func<bool> eligible, CancellationToken token)
    {
        if (review.Original is not null) { throw new InvalidOperationException("An existing-original review cannot create a new copy."); }
        return SaveAsync(review, confirmationOrigin, nativeConfirmation, eligible, token);
    }

    public async Task<AgentProfileWriteResult> SaveAsync(AgentProfileSaveReview review,
        RequestOrigin confirmationOrigin, Func<CancellationToken, Task<bool>> nativeConfirmation,
        Func<bool> eligible, CancellationToken token)
    {
        if (!ReferenceEquals(review.Owner, this) || confirmationOrigin != RequestOrigin.LocalUi)
        { throw new InvalidOperationException("Save requires a separate exact native confirmation."); }
        review.Consume();
        var publicationObserved = false;
        try
        {
            return await admission.Run(() => eligible() && review.Eligible(), async (request, session, current, admitted) =>
            {
                if (!await nativeConfirmation(admitted).ConfigureAwait(false))
                { return new AgentProfileWriteResult(AgentProfileWriteState.Cancelled, null); }
                Require(current, admitted);
                var requested = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
                    review.Original is null ? "definition.agent.create" : "definition.agent.update", SecurityAuditOutcome.Requested,
                    review.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
                    "definition.owned-agent");
                audit.Write(requested);
                try
                {
                    Task<AgentProfileWriteResult> Commit(Func<AgentProfileWriteResult> operation) =>
                        admission.Publish(request, session, current, operation, admitted).AsTask();
                    var result = await (review.Original is { } original
                        ? store.Update(original, review.Document, current, Commit, admitted)
                        : store.Create(review.Destination, review.Document, current, Commit, admitted)).ConfigureAwait(false);
                    publicationObserved = result.State == AgentProfileWriteState.Saved;
                    if (result.State is AgentProfileWriteState.Saved or AgentProfileWriteState.Unchanged && (result.Snapshot is null
                        || result.Snapshot.Destination != review.Destination
                        || !result.Snapshot.Document.Document.CopyBytes().AsSpan().SequenceEqual(review.Document.Document.CopyBytes())))
                    { throw new IOException("The exact durable saved original is unconfirmed."); }
                    Require(current, admitted);
                    audit.Write(requested.WithOutcome(result.State switch
                    {
                        AgentProfileWriteState.Saved or AgentProfileWriteState.Unchanged => SecurityAuditOutcome.Succeeded,
                        AgentProfileWriteState.Cancelled => SecurityAuditOutcome.Cancelled,
                        _ => SecurityAuditOutcome.Failed,
                    }, result.State switch
                    {
                        AgentProfileWriteState.Saved => review.Original is null ? "exact-new-file-readback" : "exact-updated-file-readback",
                        AgentProfileWriteState.Unchanged => "exact-unchanged-file-readback",
                        AgentProfileWriteState.Cancelled => "publication-cancelled",
                        _ => review.Original is null ? "destination-occupied" : "original-base-changed",
                    }));
                    return result;
                }
                catch (Exception exception)
                {
                    var observed = publicationObserved || exception is AgentProfilePublicationUnconfirmedException;
                    publicationObserved = observed;
                    audit.Write(requested.WithOutcome(observed ? SecurityAuditOutcome.Unknown
                        : exception is OperationCanceledException ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Failed,
                        observed ? "publication-observed-unconfirmed" : "publication-not-confirmed"));
                    throw;
                }
            }, token).ConfigureAwait(false);
        }
        catch (Exception exception) when (publicationObserved && exception is not AgentProfilePublicationUnconfirmedException)
        { throw new AgentProfilePublicationUnconfirmedException(exception); }
    }

    private static void Require(Func<bool> eligible, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!eligible()) { throw new InvalidOperationException("Original authoring input, host, call, control or privacy authority changed."); }
    }
}
