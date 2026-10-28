using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kora.Application.Interaction;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Network;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Kora.Tools.Network;

namespace Kora.Application.Network;

/// <summary>One original local request, one destination, one approval and at most one governed retrieval.</summary>
internal sealed class LocalWebPageInvocation(
    WebPageGet action, IHostInteractionStore store, WebPageApprovalPresenter present,
    HostRequest original, HostRevision generation, Uri destination, DateTimeOffset expiresAt,
    Func<bool> admitted, TimeProvider time, HostId<InvocationIdentity>? workflowInvocation = null,
    LocalWebPageScope? workflowScope = null, Func<CancellationToken, Task>? validateSource = null)
{
    private int used;
    internal Uri Destination => destination;
    internal bool Completed { get; private set; }
    private Func<CancellationToken, Task>? validateGranted;

    internal Task ValidateAsync(CancellationToken token) => validateGranted?.Invoke(token) ?? Task.CompletedTask;

    internal async ValueTask<CapabilityReply> InvokeAsync(ModelTurnProvenance provenance, string id,
        string input, CancellationToken token)
    {
        var parsed = CapabilityInputPolicy.Read(LocalWebPageTool.Descriptor, input);
        if (provenance.Request != original || provenance.Selection.Provider != ModelProviderIdentity.Ollama
            || !string.Equals(id, WebPageCapability.Id, StringComparison.Ordinal)
            || parsed.DenialReason is not null || parsed.WebPageAddress != destination
            || !Live(token) || Interlocked.Exchange(ref used, 1) != 0)
        { return new(CapabilityOutcome.Denied, "exact-local-page-not-admitted"); }

        var invocation = new HostRequest(original.RequestId, original.SessionId, original.TaskId,
            original.Origin, workflowInvocation ?? new(Guid.NewGuid()));
        var parent = HostActivity.RequireCurrent();
        using var activity = HostActivity.BeginRoot(invocation, HostActivityLayer.Application, HostOperation.Tool,
            [new ActivityLink(parent.Activity!.Context)]);
        try
        {
            var proposal = CreateProposal(invocation);
            // Native review owns its own activity; presentation freshness is not executable host context.
            var presented = await present(proposal, generation, destination,
                () => Admitted(token) && activity.Outcome == HostOperationOutcome.Unknown, token).ConfigureAwait(false);
            if (!Live(token) || presented.Presented.Proposal != proposal
                || presented.Presented.Key.Request != invocation || presented.Presented.SessionGeneration != generation
                || presented.Completed is not { Outcome: HostInteractionOutcome.Approved, Grant: { } grant }
                || grant.ApprovedProposal != proposal || grant.Scope != OperationGrantScope.Once
                || grant.SessionGeneration != generation
                || presented.Completed.Question is not { Status: QuestionStatus.Answered } answer
                || answer.Key.QuestionId != presented.Presented.Key.QuestionId
                || answer.Key.Request != invocation || answer.Proposal != proposal
                || answer.Draft is not { Text: null } draft || !draft.Choices.SequenceEqual(["once"])
                || answer.AnswerChannel is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
            { activity.Complete(HostOperationOutcome.Failed); return new(CapabilityOutcome.Denied, "exact-page-approval-required"); }
            var authorization = new HostAuthorizationService(store, time);
            var consumed = await authorization.ConsumeAsync(invocation, grant.Id, grant.Revision, token).ConfigureAwait(false);
            if (consumed.Outcome != HostInteractionOutcome.Consumed || consumed.Grant is not { } receipt
                || consumed.Admission is not { } admission)
            { activity.Complete(HostOperationOutcome.Failed); return new(CapabilityOutcome.Denied, "page-approval-not-current"); }
            var dispatched = await authorization.DispatchAsync(invocation, admission, token).ConfigureAwait(false);
            if (dispatched.Outcome != HostInteractionOutcome.Consumed || dispatched.Admission is not { } dispatch)
            { activity.Complete(HostOperationOutcome.Failed); return new(CapabilityOutcome.Denied, "page-admission-retired"); }

            ValueTask<bool> Current(Uri address, CancellationToken checkToken) => Check(address, checkToken, false);
            async ValueTask<bool> Check(Uri address, CancellationToken checkToken, bool continuation)
            {
                if (address != destination || !Live(checkToken)) { return false; }
                if (validateSource is not null)
                {
                    var cause = HostActivity.RequireCurrent();
                    using var sourceCheck = HostActivity.BeginRoot(original, HostActivityLayer.Application, HostOperation.Policy,
                        [new ActivityLink(cause.Activity!.Context)]);
                    try
                    {
                        await validateSource(checkToken).ConfigureAwait(false);
                        sourceCheck.Complete(HostOperationOutcome.Completed);
                    }
                    catch (OperationCanceledException) { sourceCheck.Complete(HostOperationOutcome.Cancelled); throw; }
                    catch { sourceCheck.Complete(HostOperationOutcome.Failed); throw; }
                }
                var transaction = new InteractionTransaction(store);
                var observed = await transaction.RunAsync(invocation, "approval.web-page-revalidate", snapshot =>
                {
                    var current = snapshot.Grants.SingleOrDefault(item => item.Id == receipt.Id);
                    // A later finite page observation may replace this proposal, never its consumed grant or exact dispatch.
                    return Live(checkToken) && snapshot.Session.Generation == generation
                        && InteractionTransaction.CanInteract(snapshot) && snapshot.Policy.OtherMandatoryGatesSatisfied
                        && (snapshot.Proposal == proposal
                            || continuation && workflowScope is not null && snapshot.Proposal is { } next
                                && next.Request == invocation && next.ExpiresAt == expiresAt
                                && next.Effect == HostOperationEffect.BoundedRead && next.Revision == new HostRevision(1)
                                && workflowScope.Destinations.Any(item => next.Binding == CreateBinding(invocation, item)))
                        && current == receipt
                        && snapshot.EffectivePermission == dispatch.Policy && snapshot.Admissions.Contains(dispatch)
                        && current.Status == OperationGrantStatus.Consumed
                        ? (snapshot, new(HostInteractionOutcome.Approved, "exact-page-use-current"))
                        : InteractionTransaction.Reject(snapshot, "exact-page-use-retired");
                }, checkToken).ConfigureAwait(false);
                return observed.Outcome == HostInteractionOutcome.Approved && Live(checkToken);
            }
            validateGranted = async checkToken =>
            {
                var current = HostActivity.RequireCurrent();
                using var validation = HostActivity.BeginRoot(invocation, HostActivityLayer.Application, HostOperation.Policy,
                    [new ActivityLink(current.Activity!.Context)]);
                try
                {
                    if (!await Check(destination, checkToken, true).ConfigureAwait(false))
                    { throw new InvalidOperationException("The exact page approval was revoked, expired or retired."); }
                    validation.Complete(HostOperationOutcome.Completed);
                }
                catch (OperationCanceledException)
                { validation.Complete(HostOperationOutcome.Cancelled); throw; }
                catch
                { validation.Complete(HostOperationOutcome.Failed); throw; }
            };

            var result = await action.ExecuteAsync(new(destination),
                original.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.TypedCommand,
                Current, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!await Current(destination, token).ConfigureAwait(false))
            { activity.Complete(HostOperationOutcome.Failed); return new(CapabilityOutcome.Denied, "page-approval-retired"); }
            if (result.Outcome != WebPageGetOutcome.Succeeded)
            {
                activity.Complete(HostOperationOutcome.Failed);
                return new(result.Outcome == WebPageGetOutcome.Denied ? CapabilityOutcome.Denied : CapabilityOutcome.Failed, result.Reason);
            }
            var page = new LocalWebPageResult(original, destination, result.FinalAddress!, time.GetUtcNow(),
                result.Text!, result.Truncated, result.MediaType!);
            var reply = Bound(page);
            Completed = reply.Outcome == CapabilityOutcome.Succeeded;
            activity.Complete(reply.Outcome == CapabilityOutcome.Succeeded ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
            return reply;
        }
        catch (OperationCanceledException)
        { activity.Complete(HostOperationOutcome.Cancelled); throw; }
        catch
        { activity.Complete(HostOperationOutcome.Failed); throw; }
    }

    private bool Admitted(CancellationToken token) => !token.IsCancellationRequested && admitted() && time.GetUtcNow() < expiresAt;

    private bool Live(CancellationToken token) => Admitted(token) && HostActivity.Current is { } current
        && current.Request.IsWithinIntent(original) && current.Outcome == HostOperationOutcome.Unknown
        && !current.Activity!.IsStopped;

    private HostOperationProposal CreateProposal(HostRequest invocation) =>
        new(invocation, new(Guid.NewGuid()), new(1), CreateBinding(invocation, destination), HostOperationEffect.BoundedRead, expiresAt);

    private ExactOperationBinding CreateBinding(HostRequest invocation, Uri destination)
    {
        var definition = Hash(JsonSerializer.Serialize(LocalWebPageTool.Descriptor));
        var address = WebPageAccessBinding.DestinationDigest(destination);
        return new(WebPageAccessBinding.ActionId, "builtin", "local-web-page",
            definition, address, Hash("page-not-yet-retrieved"),
            Hash(typeof(WebPageGet).Module.ModuleVersionId.ToString("D")),
            Hash(JsonSerializer.Serialize(invocation)), address,
            Hash(JsonSerializer.Serialize(new { original, generation })), address,
            Hash("inert-local-only-text-v1"), new(1));
    }

    internal static CapabilityReply Bound(LocalWebPageResult page)
    {
        var reply = new CapabilityReply(CapabilityOutcome.Succeeded, "retrieved-inert-local-page", WebPage: page);
        while (CapabilityReplyWire.GetSerializedSize(reply) > LocalWebPageTool.Descriptor.Limits.MaximumOutputUtf8Bytes)
        {
            if (page.Text.Length == 0) { return new(CapabilityOutcome.Failed, "page-provenance-exceeds-result-limit"); }
            var length = page.Text.Length / 2;
            if (length > 0 && char.IsHighSurrogate(page.Text[length - 1])) { length--; }
            page = page with { Text = page.Text[..length], Truncated = true };
            reply = reply with { WebPage = page };
        }
        return reply;
    }

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
