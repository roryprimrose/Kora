using System.Text;
using System.Text.Json;
using System.Collections.Immutable;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Artifacts;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Skills;
using Kora.Core.Storage;

namespace Kora.Application.Skills;

/// <summary>Exact owned-package review and one separately confirmed local, tool-free conversation request.</summary>
public sealed class OwnedSkillInvocationWorkflow(
    IOwnedDeclarativeSkillStore store, OwnedSkillControlAdmission admission, ISessionWorkspaceAccess access,
    ISessionWorkspaceStore sessions, SessionWorkspaceService workspace, SessionConversationService conversation,
    ISecurityAuditLog audit, TimeProvider time, IArtifactUsePolicy? usePolicy = null)
{
    public async Task<OwnedSkillUseReview> ReviewAsync(string id, string version,
        OwnedSkillAuthoringAuthority authority, Func<bool> eligible, CancellationToken token)
    {
        RequireAction(authority, eligible, token);
        OwnedDeclarativeSkillDocument.RequireIdentity(id, version);
        var revision = access.ControlRevision;
        var policyRevision = usePolicy?.Revision;
        bool Current() => authority.IsCurrent() && eligible() && access.CanControl && access.CanInspect
            && access.ControlRevision == revision && usePolicy?.Revision == policyRevision;
        var snapshot = await admission.Run(authority, Current, async (_, _, live, admitted) =>
        {
            var captured = await store.Read(id, version, admitted, OwnedSkillReadScope.PrimaryDeclaration).ConfigureAwait(false)
                ?? throw new InvalidDataException("The exact saved skill ID/version is absent.");
            Require(live, admitted);
            if (!string.Equals(captured.Document.Id, id, StringComparison.Ordinal)
                || !string.Equals(captured.Document.Version, version, StringComparison.Ordinal))
            { throw new InvalidDataException("The saved package did not resolve the exact requested identity."); }
            RequireUseAllowed(captured);
            return captured;
        }, token).ConfigureAwait(false);
        Require(Current, token);
        using var activity = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Policy, authority.Origin);
        var review = new OwnedSkillUseReview(this, snapshot, Current, time, new(activity.Activity!.Context));
        activity.Complete(HostOperationOutcome.Completed);
        return review;
    }

    public async Task ReadReferencesAsync(OwnedSkillUseReview review, OwnedSkillAuthoringAuthority authority,
        Func<bool> eligible, CancellationToken token)
    {
        try
        {
            RequireAction(authority, eligible, token);
            if (!review.RequiresReferenceConsent)
            { throw new InvalidOperationException("Review an exact unread component declaration first."); }
            var expected = review.Require(this);
            var captured = await admission.Run(authority, () => eligible() && review.IsCurrent,
                async (_, _, live, admitted) =>
                {
                    RequireUseAllowed(expected);
                    var actual = await store.Read(expected.Document.Id, expected.Document.Version, admitted,
                        expectedPrimary: expected).ConfigureAwait(false)
                        ?? throw new InvalidDataException("The reviewed package is missing.");
                    Require(live, admitted);
                    RequireUseAllowed(actual);
                    return actual;
                }, token).ConfigureAwait(false);
            Require(() => authority.IsCurrent() && eligible() && review.IsCurrent, token);
            review.CompleteReferences(this, captured);
        }
        catch { review.Dispose(); throw; }
    }

    public async Task EnableAsync(OwnedSkillUseReview review, OwnedSkillAuthoringAuthority authority,
        Func<bool> eligible, CancellationToken token)
    {
        try
        {
            RequireAction(authority, eligible, token);
            await ValidateAsync(review, authority, eligible, token).ConfigureAwait(false);
            await admission.Run(authority, () => eligible() && review.IsCurrent, async (request, session, live, admitted) =>
                await admission.Commit(request, session, live, () =>
                {
                    _ = review.Require(this);
                    var entry = Entry(request, "skills.owned-revision.enable", SecurityAuditCategory.SecurityApproval);
                    audit.Write(entry);
                    try
                    {
                        review.Enable(this);
                        audit.Write(entry.WithOutcome(SecurityAuditOutcome.Succeeded));
                        return true;
                    }
                    catch
                    {
                        review.Dispose();
                        audit.Write(entry.WithOutcome(SecurityAuditOutcome.Failed, "enable-unconfirmed"));
                        throw;
                    }
                }, admitted).ConfigureAwait(false), token).ConfigureAwait(false);
            Require(() => eligible() && review.IsCurrent, token);
        }
        catch { review.Dispose(); throw; }
    }

    public async Task<SessionPage<SessionWorkspaceEntry>> ReadTargetsAsync(Guid? after,
        OwnedSkillAuthoringAuthority authority, Func<bool> eligible, CancellationToken token)
    {
        RequireAction(authority, eligible, token);
        using var activity = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Storage, authority.Origin);
        var page = await workspace.ReadMetadataAsync(after, 25, token).ConfigureAwait(false);
        Require(() => authority.IsCurrent() && eligible(), token);
        activity.Complete(HostOperationOutcome.Completed);
        return page;
    }

    public async Task<OwnedSkillInvocation> PrepareAsync(OwnedSkillUseReview review, SessionWorkspaceEntry target,
        string originalRequest, OwnedSkillAuthoringAuthority authority, Func<bool> eligible, CancellationToken token,
        IReadOnlyDictionary<string, string>? inputs = null)
    {
        try
        {
            RequireAction(authority, eligible, token);
            if (!review.IsEnabled) { throw new InvalidOperationException("Review and separately enable the exact saved revision first."); }
            if (review.Require(this).Document.Tool != OwnedSkillToolSelection.None)
            { throw new InvalidOperationException("Tool-declaring revisions require the separate reviewed host tool pipeline."); }
            ConversationText.Validate(originalRequest, submission: true);
            var document = review.Require(this).Document;
            document.RequireComplete();
            var values = inputs?.ToImmutableDictionary(StringComparer.Ordinal) ?? ImmutableDictionary<string, string>.Empty;
            string? guidanceEvidence = null;
            if (document.InputTemplate is { } template)
            {
                var expanded = template.Expand(values, token);
                guidanceEvidence = JsonSerializer.Serialize(new { RequiredLiteralInputs = values, ExpandedGuidance = expanded });
            }
            else if (!values.IsEmpty)
            { throw new InvalidDataException("Literal native v1/v2 skills do not accept input bindings."); }
            await ValidateAsync(review, authority, eligible, token).ConfigureAwait(false);
            await RequireTargetAsync(target, eligible, token).ConfigureAwait(false);
            var request = new HostRequest(new(Guid.NewGuid()), target.Authority.SessionId, new(Guid.NewGuid()), authority.Origin);
            var context = CreateContext(review.Require(this), request, originalRequest, time.GetUtcNow(), review.ExpiresAt, guidanceEvidence);
            Require(() => eligible() && authority.IsCurrent() && review.IsCurrent, token);
            return new(review, target, originalRequest, Encoding.UTF8.GetString(context.Serialize()), authority, eligible, guidanceEvidence);
        }
        catch { review.Dispose(); throw; }
    }

    public async Task<ConversationSubmissionReceipt> InvokeAsync(OwnedSkillInvocation invocation,
        OwnedSkillAuthoringAuthority confirmation, Func<bool> eligible, CancellationToken token)
    {
        var review = invocation.Review;
        using (invocation)
        using (review)
        {
            RequireAction(confirmation, eligible, token);
            var body = invocation.Consume();
            var guidanceEvidence = invocation.GuidanceEvidence;
            review.Consume(this);
            bool Current() => eligible() && confirmation.IsCurrent() && invocation.Authority.IsCurrent()
                && invocation.Current() && review.IsCurrent;
            using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), invocation.Target.Authority.SessionId,
                new(Guid.NewGuid()), invocation.Origin), HostActivityLayer.Application, HostOperation.Request, [review.Cause]);
            try
            {
                Task Validate(CancellationToken cancellation) => ValidateAsync(review, invocation.Authority, Current, cancellation);
                await Validate(token).ConfigureAwait(false);
                await RequireTargetAsync(invocation.Target, Current, token).ConfigureAwait(false);
                var snapshot = review.Require(this);
                var entry = Entry(HostActivity.RequireCurrent().Request, "skills.owned-revision.invoke", SecurityAuditCategory.ProtectedOperation);
                audit.Write(entry);
                try
                {
                    var receipt = await conversation.SubmitOwnedSkillAsync(invocation.Target, body,
                        (request, now) => CreateContext(snapshot, request, body, now, review.ExpiresAt, guidanceEvidence),
                        Current, Validate, [review.Cause], invocation.Origin, token).ConfigureAwait(false);
                    await Validate(token).ConfigureAwait(false);
                    await RequireTargetAsync(invocation.Target, Current, token).ConfigureAwait(false);
                    audit.Write(entry.WithOutcome(receipt.Message.Outcome == ModelTurnOutcome.Succeeded
                        ? SecurityAuditOutcome.Succeeded : SecurityAuditOutcome.Failed, "retained-terminal"));
                    activity.Complete(receipt.Message.Outcome == ModelTurnOutcome.Succeeded ? HostOperationOutcome.Completed
                        : receipt.Message.Outcome == ModelTurnOutcome.Cancelled ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
                    return receipt;
                }
                catch
                {
                    audit.Write(entry.WithOutcome(SecurityAuditOutcome.Failed, "invocation-unconfirmed"));
                    throw;
                }
            }
            catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
            catch { activity.Complete(HostOperationOutcome.Failed); throw; }
        }
    }

    internal async Task ValidateAsync(OwnedSkillUseReview review, OwnedSkillAuthoringAuthority authority,
        Func<bool> eligible, CancellationToken token)
    {
        try
        {
            Require(() => authority.IsCurrent() && eligible() && review.IsCurrent, token);
            await admission.Run(authority, () => eligible() && review.IsCurrent, async (_, _, live, admitted) =>
            {
                await ValidateAdmittedAsync(review, authority, live, admitted).ConfigureAwait(false);
                return true;
            }, token).ConfigureAwait(false);
            Require(() => authority.IsCurrent() && eligible() && review.IsCurrent, token);
        }
        catch { review.Dispose(); throw; }
    }

    private void RequireUseAllowed(OwnedDeclarativeSkillSnapshot snapshot)
    {
        if (snapshot.Document.ReferenceDeclaration is null) { return; }
        if (usePolicy is null)
        { throw new InvalidOperationException("Owned component use requires the current host exclusion policy."); }
        var root = store.Destination(snapshot.Document);
        if (snapshot.Document.ReferenceDeclaration.Paths.Prepend("instructions.md").Prepend("manifest.yaml")
            .Any(relative => usePolicy.IsDisabled(ArtifactUseIdentity.File(Path.Combine(root, relative)))))
        { throw new InvalidOperationException("A selected package component is excluded from Kora use. Review again after independent re-enablement."); }
    }

    internal async Task ValidateAdmittedAsync(OwnedSkillUseReview review, OwnedSkillAuthoringAuthority authority,
        Func<bool> eligible, CancellationToken token)
    {
        Require(() => authority.IsCurrent() && eligible() && review.IsCurrent, token);
        var expected = review.Require(this);
        expected.Document.RequireComplete();
        var actual = await store.Read(expected.Document.Id, expected.Document.Version, token,
            expectedPrimary: expected).ConfigureAwait(false);
        Require(() => authority.IsCurrent() && eligible() && review.IsCurrent, token);
        if (actual is null || !expected.IsSameRevision(actual))
        { throw new InvalidDataException("The exact private saved package changed, was replaced or is missing. Review again."); }
        RequireUseAllowed(actual);
    }

    private async Task RequireTargetAsync(SessionWorkspaceEntry target, Func<bool> eligible, CancellationToken token)
    {
        Require(eligible, token);
        if (!target.Authority.IsActive || await sessions.ReadMetadataAsync(target.Authority.SessionId, token).ConfigureAwait(false) != target)
        { throw new InvalidOperationException("Select exact current metadata for one existing Active conversation."); }
        Require(eligible, token);
    }

    private void RequireAction(OwnedSkillAuthoringAuthority authority, Func<bool> eligible, CancellationToken token)
    {
        SessionConversationService.RequireOriginalSource(authority.Origin);
        Require(() => authority.IsCurrent() && eligible(), token);
    }

    private void Require(Func<bool> eligible, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!eligible() || !access.CanControl || !access.CanInspect)
        { throw new InvalidOperationException("Private original-input owned skill use authority is unavailable."); }
    }

    private static SecurityAuditEvent Entry(HostRequest request, string action, SecurityAuditCategory category) =>
        new(request.RequestId.Value, category, action, SecurityAuditOutcome.Requested,
            request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
            "definition.owned-skill");

    private static ModelContextEnvelope CreateContext(OwnedDeclarativeSkillSnapshot snapshot, HostRequest request,
        string body, DateTimeOffset now, DateTimeOffset expires, string? guidanceEvidence) =>
        OwnedSkillGuidanceContext.Create(snapshot.Document, request, body, now, expires, guidanceEvidence);
}
