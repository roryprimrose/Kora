using System.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.KnowledgeSources;
using Microsoft.Extensions.Logging;

namespace Kora.Application.KnowledgeSources;

public sealed partial class KnowledgeSourceAnswerWorkflow(KnowledgeSourceManager manager,
    ILocalFileRetrieval retrieval, ILocalFileModelUsePolicy sourceUse, IModelContextRunner model,
    ISecurityAuditLog audit, TimeProvider time, ILogger<KnowledgeSourceAnswerWorkflow> logger)
{
    private KnowledgeSourceAnswerUse? use;

    public async Task<KnowledgeSourceAnswerPreparation> PrepareAsync(KnowledgeSource exact, string question,
        Func<bool> eligible, CancellationToken token)
    {
        var host = HostActivity.RequireCurrent();
        if (host.Request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || host.Request.InvocationId is not null || host.Outcome != HostOperationOutcome.Unknown || !eligible())
        { return new(LocalFileAnswerOutcome.Denied); }
        KnowledgeSourceAnswerUse? selectedUse = null;
        var result = await Audited("review", async () =>
        {
            if (!LocalFileRetrievalPolicy.TryQuery(question, out _))
            { return new KnowledgeSourceAnswerPreparation(LocalFileAnswerOutcome.InvalidQuestion); }
            selectedUse = await manager.OpenAnswerUseAsync(exact, eligible, token).ConfigureAwait(false);
            return await selectedUse.RunAsync(async cancellation =>
            {
                var snapshot = selectedUse.Revision;
                var found = await Task.Run(() => retrieval.Search(snapshot, snapshot.Reference, question,
                    time.GetUtcNow(), cancellation), cancellation).ConfigureAwait(false);
                await selectedUse.ValidateAsync(cancellation).ConfigureAwait(false);
                if (found.Outcome == LocalFileSearchOutcome.NoMatch)
                { return new KnowledgeSourceAnswerPreparation(LocalFileAnswerOutcome.NoEvidence); }
                var target = LocalContextSessionTarget.ReviewedFresh(host.Request, HostRequest.Create(host.Request.Origin));
                var review = new KnowledgeSourceAnswerReview(exact, snapshot, question, found, target);
                selectedUse.Review = review;
                return new KnowledgeSourceAnswerPreparation(LocalFileAnswerOutcome.Succeeded, review);
            }, token).ConfigureAwait(false);
        }, value => value.Outcome, () => new(LocalFileAnswerOutcome.Unavailable)).ConfigureAwait(false);
        if (result.Review is null && selectedUse is not null)
        { await manager.ReleaseAnswerUseAsync(selectedUse).ConfigureAwait(false); }
        else if (result.Review is not null) { use = selectedUse; }
        return result;
    }

    public async Task<LocalFileAnswerResult> AnswerAsync(KnowledgeSourceAnswerReview review, Func<bool> eligible,
        CancellationToken token)
    {
        var input = HostActivity.RequireCurrent();
        if (input.Request.Origin != RequestOrigin.LocalUi || input.Request.InvocationId is not null
            || input.Outcome != HostOperationOutcome.Unknown || !eligible()
            || use is not { Review: { } prepared } exact || !review.IsFromSameRetrieval(prepared)
            || !review.IsCurrent(time.GetUtcNow()))
        { return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Denied); }
        exact.Review = review;
        if (!exact.IsCurrent || !review.TryConsume())
        { return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Denied); }
        ActivityLink[] causes = review.Revision.Review.Cause == default
            ? [new(input.Activity!.Context)] : [new(input.Activity!.Context), new(review.Revision.Review.Cause)];
        using var activity = HostActivity.BeginRoot(review.Target.SourceRequest, HostActivityLayer.Application,
            HostOperation.Request, causes);
        var result = await Audited("local", () => exact.RunAsync(async cancellation =>
        {
            await exact.ValidateAsync(cancellation).ConfigureAwait(false);
            var remaining = review.ExpiresAt - time.GetUtcNow();
            if (remaining <= TimeSpan.Zero) { return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Stale); }
            using var expiry = new CancellationTokenSource(remaining, time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, expiry.Token);
            cancellation = linked.Token;
            bool Current() => eligible() && exact.IsCurrent;
            await exact.ValidateAsync(cancellation).ConfigureAwait(false);
            foreach (var file in review.Revision.Files)
            {
                var citations = review.Search.Citations.Where(item => item.Source == file.Reference).ToArray();
                if (citations.Length == 0) { continue; }
                var selected = review.Search with
                {
                    Citations = Array.AsReadOnly(citations), MatchingChunks = citations.Length, Truncated = false,
                };
                var decision = await sourceUse.AdmitAsync(file, review.Question, selected, cancellation).ConfigureAwait(false);
                await exact.ValidateAsync(cancellation).ConfigureAwait(false);
                if (decision != LocalFileModelUseDecision.Allowed)
                {
                    return LocalFileAnswerResult.Empty(decision is LocalFileModelUseDecision.Unavailable
                        or LocalFileModelUseDecision.SourceUsePolicyMissing ? LocalFileAnswerOutcome.SourceUseUnavailable
                            : LocalFileAnswerOutcome.Denied) with { SourceUseDecision = decision };
                }
            }
            var admission = await model.AdmitAsync(review.Target, Current, cancellation, exact.ValidateAsync).ConfigureAwait(false);
            if (admission.Turn is not { } admitted)
            { return Receipt(review, null, admission.Rejection ?? throw new InvalidOperationException("Missing local admission result.")); }
            await using var turn = admitted;
            await exact.ValidateAsync(cancellation).ConfigureAwait(false);
            var context = new KnowledgeSourceAnswerContext(review, turn.Request, turn.SessionGeneration, time.GetUtcNow());
            if (context.Envelope.Serialize().Length > ModelContextEnvelope.MaximumInputUtf8Bytes)
            { return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.ContextLimit); }
            var receipt = await turn.RunAsync(context.Envelope, cancellation).ConfigureAwait(false);
            await exact.ValidateAsync(cancellation).ConfigureAwait(false);
            if (!Current()) { return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Stale); }
            return Receipt(review, context, receipt);
        }, token), value => value.Outcome, () => LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Unavailable)).ConfigureAwait(false);
        if (!eligible() || !exact.IsCurrent)
        { result = LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Stale); }
        activity.Complete(ActivityOutcome(result.Outcome));
        return result;
    }

    private async Task<T> Audited<T>(string operation, Func<Task<T>> action, Func<T, LocalFileAnswerOutcome> outcome,
        Func<T> unavailable)
    {
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Tool);
        var entry = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
            "knowledge.answer." + operation, SecurityAuditOutcome.Requested,
            HostActivity.RequireCurrent().Request.Origin == RequestOrigin.ActivatedVoice
                ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser, "knowledge.selected-retained");
        audit.Write(entry);
        T result;
        try { result = await action().ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            audit.Write(entry.WithOutcome(SecurityAuditOutcome.Cancelled));
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException
            or InvalidDataException or System.Security.Cryptography.CryptographicException)
        {
            Failure(exception.GetType().Name);
            result = unavailable();
        }
        catch
        {
            audit.Write(entry.WithOutcome(SecurityAuditOutcome.Failed));
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
        var terminal = outcome(result);
        audit.Write(entry.WithOutcome(terminal is LocalFileAnswerOutcome.Succeeded or LocalFileAnswerOutcome.NoEvidence
            or LocalFileAnswerOutcome.Unsupported ? SecurityAuditOutcome.Succeeded : SecurityAuditOutcome.Failed,
            terminal.ToString().ToLowerInvariant()));
        activity.Complete(ActivityOutcome(terminal));
        return result;
    }

    private static HostOperationOutcome ActivityOutcome(LocalFileAnswerOutcome outcome) =>
        outcome is LocalFileAnswerOutcome.Succeeded or LocalFileAnswerOutcome.NoEvidence or LocalFileAnswerOutcome.Unsupported
            ? HostOperationOutcome.Completed : HostOperationOutcome.Failed;

    private static LocalFileAnswerResult Receipt(KnowledgeSourceAnswerReview review, KnowledgeSourceAnswerContext? context,
        LocalContextTurnReceipt receipt)
    {
        if (!ReferenceEquals(receipt.Request, review.Target.Request)
            || !Enum.IsDefined(receipt.Outcome) || !Enum.IsDefined(receipt.Reason)
            || receipt.Outcome == ModelTurnOutcome.Succeeded
                && (receipt.DurableState != HostTaskState.Succeeded || receipt.Reason != LocalContextTurnReason.Completed))
        { return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.InvalidResponse); }
        var result = context is not null && receipt.ModelResult is { } modelResult && modelResult.Outcome == receipt.Outcome
            ? context.Resolve(modelResult) : LocalFileAnswerResult.Empty(receipt.Outcome switch
            {
                ModelTurnOutcome.Succeeded => LocalFileAnswerOutcome.InvalidResponse,
                ModelTurnOutcome.Cancelled => LocalFileAnswerOutcome.Cancelled,
                ModelTurnOutcome.Denied or ModelTurnOutcome.DeniedEgress => LocalFileAnswerOutcome.Denied,
                _ => LocalFileAnswerOutcome.Unavailable,
            });
        return result with { ExecutionReason = receipt.Reason, ExecutionState = receipt.DurableState };
    }
}
