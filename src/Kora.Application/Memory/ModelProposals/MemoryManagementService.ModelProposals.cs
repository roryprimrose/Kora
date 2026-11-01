using Kora.Application.Memory.ModelProposals;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Core.Memory.ModelProposals;
using Kora.Core.Storage;

namespace Kora.Application.Memory;

internal sealed partial class MemoryManagementService
{
    internal bool ModelProposalCurrent(long control, DateTimeOffset expires) =>
        !disposed && access.Underlying.CanInspect && access.Underlying.CanControl
        && access.ControlRevision == control && contextTime.GetUtcNow() < expires;

    internal async Task<ModelMemoryProposalReview> ReviewModelProposalAsync(SessionWorkspaceEntry target,
        ConversationMessage source, Func<bool> admitted, CancellationToken token)
    {
        source.Validate();
        if (source.Outcome != Kora.Core.Dependencies.ModelTurnOutcome.Succeeded)
        { throw new InvalidDataException("Only a genuine retained successful ASSISTANT answer can propose memory."); }
        var candidate = ModelMemoryProposal.Parse(source.AssistantResponse!.Body);
        var trigger = RequireModelProposalInput();
        if (trigger == source.Request.RequestId)
        { throw new InvalidOperationException("The source turn cannot review or approve itself."); }
        ModelMemoryProposalReview? review = null;
        try
        {
            await RunMemorySourceReviewAsync(target, admitted, async (_, live) =>
            {
                await sessions.ReadSettingSuggestionAsync(target, source, live, token).ConfigureAwait(false);
                var rows = await Admission.ObserveSessionAsync(token).ConfigureAwait(false);
                if (rows.Any(row => row.Lineage.Origin == MemoryProposalOrigin.Model
                    && row.Lineage.Request == source.Request))
                { throw new InvalidOperationException("This exact model-memory source already has a candidate or admission. Inspect session memories."); }
                await sessions.ReadSettingSuggestionAsync(target, source, live, token).ConfigureAwait(false);
                var result = await Admission.ProposeAsync(candidate, MemoryScope.Session(target.Authority.SessionId),
                    MemoryProposalOrigin.Model, token, source.Request).ConfigureAwait(false);
                if (result.Outcome != MemoryOutcome.Succeeded)
                { throw new InvalidOperationException("Model-memory proposal not created: " + result.Reason + "."); }
                if (!live()) { Admission.RetireModelProposal(result.Record!); throw new OperationCanceledException("Proposal source revoked."); }
                review = new(this, target, source, result.Record!, trigger, access.ControlRevision,
                    contextTime.GetUtcNow().AddSeconds(60), admitted);
                return new("observed", "Exact inert model proposal; no durable memory body or admission.");
            }, token).ConfigureAwait(false);
        }
        catch
        {
            review?.Dispose();
            throw;
        }
        return review!;
    }

    internal async Task<MemoryResult> ActOnModelProposalAsync(ModelMemoryProposalReview review, bool admit,
        bool accept, CancellationToken token)
    {
        var trigger = RequireModelProposalInput();
        if (!ReferenceEquals(review.Owner, this))
        { throw new InvalidOperationException("The model-memory review belongs to another owner."); }
        var record = review.Take(admit, trigger);
        MemoryResult? result = null;
        try
        {
            await RunMemorySourceReviewAsync(review.Target, () => review.Current, async (_, live) =>
            {
                await sessions.ReadSettingSuggestionAsync(review.Target, review.Source, live, token).ConfigureAwait(false);
                var rows = await Admission.ObserveSessionAsync(token).ConfigureAwait(false);
                if (!rows.Contains(record))
                { throw new InvalidOperationException("The exact model-memory candidate changed or was withdrawn."); }
                await sessions.ReadSettingSuggestionAsync(review.Target, review.Source, live, token).ConfigureAwait(false);
                result = admit
                    ? await Admission.AdmitAsync(record.Id, record.Revision, token).ConfigureAwait(false)
                    : await Admission.ReviewAsync(record.Id, record.Revision, accept, token).ConfigureAwait(false);
                if (result.Record is { } changed) { review.Transitioned(changed); }
                return new(result.Outcome.ToString(), result.Reason.ToString());
            }, token).ConfigureAwait(false);
            if (!admit && accept && result!.Outcome == MemoryOutcome.Succeeded) { review.Accepted(result.Record!); }
            else { review.Dispose(); }
            return result!;
        }
        catch
        {
            review.Dispose();
            throw;
        }
    }

    private static HostId<RequestIdentity> RequireModelProposalInput()
    {
        var input = HostActivity.RequireCurrent();
        if (input.Outcome != HostOperationOutcome.Unknown
            || input.Request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || input.Request.InvocationId is not null)
        { throw new InvalidOperationException("Model-memory review requires live original-user input, not a model callback."); }
        return input.Request.RequestId;
    }

    private async Task RunMemorySourceReviewAsync(SessionWorkspaceEntry target, Func<bool> admitted,
        Func<HostRequest, Func<bool>, ValueTask<MemoryCommandResult>> operation, CancellationToken token)
    {
        var issuer = HostActivity.RequireCurrent();
        var input = issuer.Request;
        if (input.SessionId != target.Authority.SessionId)
        { throw new InvalidOperationException("Model-memory input belongs to another session."); }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        token = linked.Token;
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var control = access.ControlRevision;
            bool Live() => !disposed && !token.IsCancellationRequested && access.ControlRevision == control
                && !issuer.Activity!.IsStopped && issuer.Outcome == HostOperationOutcome.Unknown
                && access.Underlying.CanInspect && access.Underlying.CanControl && admitted();
            access.Admitted = Live;
            await sessions.ExecuteMemoryControlAsync(target.Authority.SessionId, input.Origin, Live, operation, token)
                .ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!Live()) { throw new InvalidOperationException("Model-memory result unconfirmed after source closure. Inspect memory receipts; no rollback or retry is claimed."); }
        }
        finally { access.Admitted = static () => false; gate.Release(); }
    }
}
