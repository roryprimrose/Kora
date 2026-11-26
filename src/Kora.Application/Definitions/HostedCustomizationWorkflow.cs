using System.Diagnostics;

using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Conversation;
using Kora.Core.Customizations;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;

namespace Kora.Application.Definitions;

/// <summary>Owns and joins one selected-source read, review and canonical hosted conversation submission.</summary>
public sealed class HostedCustomizationWorkflow : IAsyncDisposable
{
    private readonly CustomizationManagementService management;
    private readonly SessionWorkspaceService workspace;
    private readonly SessionConversationService conversation;
    private readonly IModelTurnExecution execution;
    private readonly ISecurityAuditLog audit;
    private readonly TimeProvider time;
    private readonly Lock gate = new();
    private HostedCustomizationReview? pending;
    private readonly ProviderService provider;
    private CancellationTokenSource? cancellation;
    private Task running = Task.CompletedTask;
    private long revision;
    private bool closed;

    public HostedCustomizationWorkflow(CustomizationManagementService management, SessionWorkspaceService workspace,
        SessionConversationService conversation, IModelTurnExecution execution, ISecurityAuditLog audit, TimeProvider time,
        ProviderService provider)
    {
        if (provider is not (ProviderService.Copilot or ProviderService.Claude))
        { throw new ArgumentOutOfRangeException(nameof(provider)); }
        this.provider = provider;
        this.management = management; this.workspace = workspace; this.conversation = conversation;
        this.execution = execution; this.audit = audit; this.time = time;
        management.UseChanged += SourceUseChanged;
        workspace.SessionMetadataChanging += TargetChanged;
        workspace.SessionLifecycleChanged += TargetChanged;
        workspace.SessionRetired += TargetChanged;
    }

    public Task<HostedCustomizationReview> ReviewAsync(CustomizationSource source, string targetName, string body,
        RequestOrigin origin, Func<string, CancellationToken, Task<bool>>? authorizeRead,
        Func<bool> eligible, CancellationToken token) =>
        RunAsync(async (current, cancellationToken) =>
        {
            SessionConversationService.RequireOriginalSource(origin);
            ConversationText.Validate(body, submission: true);
            if (authorizeRead is null || source.Kind is not (CustomizationKind.Skill or CustomizationKind.Agent)
                || source.UnavailableReason is not null || management.IsDisabled(source))
            { throw new InvalidOperationException("An available enabled exact skill/agent and a source-read presenter are required."); }
            var disclosure = "Read only the exact selected source for this one " + provider + " review?\n" + source.Location
                + "\nPrimary file: " + source.PrimaryFile
                + "\nBounded package read: at most 32 files and 1 MiB. Only a single-file instruction-only skill or tool-free reference-free v1 agent is compatible."
                + (provider == ProviderService.Claude ? "\nClaude receives a lower-trust USER copy, not native agent or skill activation." : "\nCopilot uses the exact native selection.")
                + "\nConsent includes freshness re-reads until this operation retires. It does NOT approve hosted disclosure, retention, network, tools or runtime enablement.";
            if (!await authorizeRead(disclosure, cancellationToken).ConfigureAwait(false))
            { throw new OperationCanceledException("Selected source reading was not approved."); }
            Require(current, cancellationToken);
            var snapshot = await management.Read(source, false, current, cancellationToken).ConfigureAwait(false);
            Require(current, cancellationToken);
            if (snapshot.Source != source || management.IsDisabled(source))
            { throw new InvalidOperationException("The exact selected source changed or was disabled."); }
            var selection = CopilotCustomizationSelection.Parse(snapshot);
            SessionWorkspaceEntry? target = null;
            Guid? after = null;
            HashSet<Guid> cursors = [];
            do
            {
                var page = await workspace.ReadMetadataAsync(after, 25, cancellationToken).ConfigureAwait(false);
                Require(current, cancellationToken);
                foreach (var candidate in page.Records.Where(item => item.Authority.IsActive
                    && string.Equals(item.Metadata?.Name.Value, targetName, StringComparison.OrdinalIgnoreCase)))
                {
                    if (target is not null) { throw new InvalidOperationException("The Active conversation name is ambiguous; no target was substituted."); }
                    target = candidate;
                }
                after = page.Next;
                if (after is { } cursor && !cursors.Add(cursor))
                { throw new InvalidDataException("Conversation metadata paging did not advance."); }
            } while (after is not null);
            var exact = target ?? throw new InvalidOperationException("Choose one existing uniquely named Active conversation.");
            var review = new HostedCustomizationReview(snapshot, selection, exact, body, origin, time.GetUtcNow().AddMinutes(2), provider);
            await Validate(review, current, cancellationToken).ConfigureAwait(false);
            lock (gate) { Require(current, cancellationToken); pending = review; }
            return review;
        }, eligible, token, reviewing: true);

    public Task<HostedCustomizationReview> ApproveHostedSourceAsync(Guid expectedReview, Func<bool> eligible, CancellationToken token) =>
        RunAsync(async (current, cancellationToken) =>
        {
            var review = Pending(expectedReview);
            SessionConversationService.RequireOriginalSource(review.Origin);
            var entry = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.SecurityApproval,
                provider == ProviderService.Copilot ? "definition.copilot.hosted-copy" : "definition.claude.hosted-copy",
                SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser, review.Snapshot.Source.Id);
            audit.Write(entry);
            try
            {
                await Validate(review, current, cancellationToken).ConfigureAwait(false);
                lock (gate) { Require(current, cancellationToken); review.HostedSourceApproved = true; }
                audit.Write(entry.WithOutcome(SecurityAuditOutcome.Succeeded));
                return review;
            }
            catch { audit.Write(entry.WithOutcome(SecurityAuditOutcome.Denied)); throw; }
        }, eligible, token);

    public Task<ConversationSubmissionReceipt> ConfirmAsync(Guid expectedReview, Func<bool> eligible, CancellationToken token) =>
        RunAsync(async (current, cancellationToken) =>
        {
            var review = Pending(expectedReview);
            SessionConversationService.RequireOriginalSource(review.Origin);
            lock (gate)
            {
                if (!review.HostedSourceApproved) { throw new InvalidOperationException("Separate affirmative hosted-source consent is required before retention."); }
                pending = null;
            }
            await Validate(review, current, cancellationToken).ConfigureAwait(false);
            ActivityLink[] links = HostActivity.Current?.Activity is { } parent ? [new(parent.Context)] : [];
            try
            {
                var receipt = provider == ProviderService.Copilot
                    ? await conversation.SubmitReviewedCopilotCustomizationAsync(review.Target, review.Body,
                        review.NativeSelection, current, cancellation => Validate(review, current, cancellation),
                        links, review.Origin, cancellationToken).ConfigureAwait(false)
                    : await conversation.SubmitReviewedClaudeCustomizationAsync(review.Target, review.Body,
                        review.SelectedCopy!, current, cancellation => Validate(review, current, cancellation),
                        links, review.Origin, cancellationToken).ConfigureAwait(false);
                await Validate(review, current, cancellationToken).ConfigureAwait(false);
                return receipt;
            }
            finally { await execution.WaitForQuiescenceAsync(CancellationToken.None).ConfigureAwait(false); }
        }, eligible, token);

    private HostedCustomizationReview Pending(Guid expected)
    {
        lock (gate)
        {
            return pending is { } review && review.Id == expected ? review
                : throw new InvalidOperationException("The exact review is stale, absent or already consumed.");
        }
    }

    private async Task Validate(HostedCustomizationReview review, Func<bool> current, CancellationToken token)
    {
        bool Live() => current() && review.ExpiresAt > time.GetUtcNow() && !management.IsDisabled(review.Snapshot.Source);
        Require(Live, token);
        await management.CaptureAuthoringSourceValidation(review.Snapshot.Source, review.Snapshot, Live)(token).ConfigureAwait(false);
        Require(Live, token);
        var target = await workspace.ReadExactMetadataAsync(review.Target.Authority.SessionId, token).ConfigureAwait(false);
        Require(Live, token);
        if (target != review.Target || !target.Authority.IsActive)
        { throw new InvalidOperationException("The reviewed Active conversation changed; no target was substituted."); }
        await management.CaptureAuthoringSourceValidation(review.Snapshot.Source, review.Snapshot, Live)(token).ConfigureAwait(false);
        Require(Live, token);
    }

    private Task<T> RunAsync<T>(Func<Func<bool>, CancellationToken, Task<T>> operation, Func<bool> eligible,
        CancellationToken token, bool reviewing = false)
    {
        CancellationTokenSource source;
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        long captured;
        lock (gate)
        {
            if (closed || cancellation is not null || reviewing && pending is not null)
            { throw new InvalidOperationException("Cancel and join the existing selected-source operation before another."); }
            source = CancellationTokenSource.CreateLinkedTokenSource(token);
            cancellation = source; running = completion.Task; captured = revision;
        }
        bool Current() { lock (gate) { return !closed && captured == revision && eligible(); } }
        return ExecuteAsync();
        async Task<T> ExecuteAsync()
        {
            try { Require(Current, source.Token); return await operation(Current, source.Token).ConfigureAwait(false); }
            finally
            {
                lock (gate) { cancellation = null; source.Dispose(); completion.SetResult(); }
            }
        }
    }

    public Task StopAsync()
    {
        lock (gate)
        {
            revision++; pending = null; cancellation?.Cancel();
#pragma warning disable VSTHRD003 // Return only this workflow's published completion; cancellation does not release ownership.
            return running;
#pragma warning restore VSTHRD003
        }
    }

    private void SourceUseChanged(object? sender, EventArgs args) => _ = StopAsync();
    private void TargetChanged(HostId<SessionIdentity> session) => _ = StopAsync();

    public async ValueTask DisposeAsync()
    {
        lock (gate) { closed = true; }
        management.UseChanged -= SourceUseChanged;
        workspace.SessionMetadataChanging -= TargetChanged;
        workspace.SessionLifecycleChanged -= TargetChanged;
        workspace.SessionRetired -= TargetChanged;
        await StopAsync().ConfigureAwait(false);
    }

    private static void Require(Func<bool> current, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!current()) { throw new InvalidOperationException("The exact selected-source private review admission expired or was revoked."); }
    }
}
