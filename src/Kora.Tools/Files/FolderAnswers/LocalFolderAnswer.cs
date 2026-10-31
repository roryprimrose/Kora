using Kora.Core.Auditing;
using Kora.Core.Context;
using Kora.Core.Context.FolderAnswers;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Microsoft.Extensions.Logging;

namespace Kora.Tools.Files.FolderAnswers;

public sealed partial class LocalFolderAnswer(LocalFilePreview preview, ILocalFileRetrieval retrieval,
    ILocalFileModelUsePolicy sourceUse, IModelContextRunner model, ISecurityAuditLog audit,
    TimeProvider time, ILogger<LocalFolderAnswer> logger)
{
    public async Task<FolderAnswerPreparation> PrepareAsync(LocalFolderReference exactSource, string question,
        LocalContextSessionTarget target, Func<bool> canPresent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exactSource);
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(canPresent);
        if (!AdmittedInput(target)) { return new(LocalFileAnswerOutcome.Denied); }
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Tool);
        var requested = Requested("folder.answer.review");
        FolderAnswerPreparation result;
        try
        {
            audit.Write(requested);
            result = await preview.UseFolderAnswerAsync(exactSource, canPresent, outcome => new FolderAnswerPreparation(outcome),
                async (source, generation, current, token) =>
                {
                    if (!LocalFileRetrievalPolicy.TryQuery(question, out _)) { return new(LocalFileAnswerOutcome.InvalidQuestion); }
                    var search = await Task.Run(() => retrieval.Search(source, exactSource, question,
                        LocalFileRetrievalMode.LexicalLines, time.GetUtcNow(), token), token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (!current()) { return new(LocalFileAnswerOutcome.Stale); }
                    if (search.Outcome == LocalFileSearchOutcome.NoMatch) { return new(LocalFileAnswerOutcome.NoEvidence); }
                    try { return new(LocalFileAnswerOutcome.Succeeded, new(source, question, search, target)); }
                    catch (InvalidDataException) { return new(LocalFileAnswerOutcome.Denied); }
                }, value => Commit(requested, value.Outcome), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            Failure(logger, exception.GetType().Name);
            result = new(LocalFileAnswerOutcome.Unavailable);
        }
        Complete(activity, result.Outcome);
        return result;
    }

    public async Task<LocalFileAnswerResult> AnswerAsync(FolderAnswerReview review, Func<bool> canPresent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(canPresent);
        if (!AdmittedInput(review.Target) || !review.IsCurrent(time.GetUtcNow()) || !review.TryConsume())
        {
            return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Denied);
        }
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Tool);
        var requested = Requested("folder.answer.local");
        LocalFileAnswerResult result;
        try
        {
            audit.Write(requested);
            bool ReviewedCurrent() => canPresent() && review.IsCurrent(time.GetUtcNow());
            result = await preview.UseFolderAnswerAsync(review.Source.Reference, ReviewedCurrent, LocalFileAnswerResult.Empty,
                async (source, _, current, token) =>
                {
                    if (!ReferenceEquals(source, review.Source)) { return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Stale); }
                    foreach (var file in source.Files)
                    {
                        var citations = review.Search.Citations.Where(citation => citation.Source == file.Reference).ToArray();
                        if (citations.Length == 0) { continue; }
                        var selected = review.Search with
                        {
                            Citations = Array.AsReadOnly(citations),
                            MatchingChunks = citations.Length,
                            Truncated = false,
                        };
                        var decision = await sourceUse.AdmitAsync(file, review.Question, selected, token).ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                        if (!current()) { return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Stale); }
                        if (decision != LocalFileModelUseDecision.Allowed)
                        {
                            return LocalFileAnswerResult.Empty(decision is LocalFileModelUseDecision.Unavailable
                                or LocalFileModelUseDecision.SourceUsePolicyMissing ? LocalFileAnswerOutcome.SourceUseUnavailable
                                    : LocalFileAnswerOutcome.Denied) with { SourceUseDecision = decision };
                        }
                    }
                    var admission = await model.AdmitAsync(review.Target, current, token).ConfigureAwait(false);
                    if (admission.Turn is not { } admitted)
                    {
                        return ResolveReceipt(review.Target.Request, null, admission.Rejection
                            ?? throw new InvalidOperationException("No admitted lease or rejection."));
                    }
                    await using var turn = admitted;
                    token.ThrowIfCancellationRequested();
                    if (!current()) { return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Stale); }
                    if (!ReferenceEquals(turn.Request, review.Target.Request) || turn.SessionGeneration.Value <= 0)
                    {
                        return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Denied);
                    }
                    var binding = new FolderAnswerContext(review, turn.Request, turn.SessionGeneration, time.GetUtcNow());
                    if (binding.Envelope.Serialize().Length > ModelContextEnvelope.MaximumInputUtf8Bytes)
                    {
                        return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.ContextLimit);
                    }
                    var receipt = await turn.RunAsync(binding.Envelope, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (!current()) { return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Stale); }
                    return ResolveReceipt(turn.Request, binding, receipt);
                }, value => Commit(requested, value.Outcome), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            Failure(logger, exception.GetType().Name);
            result = LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Unavailable);
        }
        Complete(activity, result.Outcome);
        return result;
    }

    private static bool AdmittedInput(LocalContextSessionTarget target)
    {
        var context = HostActivity.RequireCurrent();
        return context.Outcome == HostOperationOutcome.Unknown && context.Request.Origin == RequestOrigin.LocalUi
            && context.Request.InvocationId is null && ReferenceEquals(context.Request, target.SourceRequest);
    }

    private static SecurityAuditEvent Requested(string action) => new(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
        action, SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser, "folder.answer.selected-revision");

    private void Commit(SecurityAuditEvent requested, LocalFileAnswerOutcome outcome) =>
        audit.Write(requested.WithOutcome(outcome is LocalFileAnswerOutcome.Succeeded or LocalFileAnswerOutcome.Unsupported
            or LocalFileAnswerOutcome.NoEvidence ? SecurityAuditOutcome.Succeeded
                : outcome == LocalFileAnswerOutcome.Cancelled ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Failed,
            outcome.ToString().ToLowerInvariant()));

    private static void Complete(HostActivity activity, LocalFileAnswerOutcome outcome) =>
        activity.Complete(outcome is LocalFileAnswerOutcome.Succeeded or LocalFileAnswerOutcome.Unsupported or LocalFileAnswerOutcome.NoEvidence
            ? HostOperationOutcome.Completed : outcome == LocalFileAnswerOutcome.Cancelled
                ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);

    private static LocalFileAnswerResult ResolveReceipt(HostRequest request, FolderAnswerContext? binding, LocalContextTurnReceipt receipt)
    {
        if (!ReferenceEquals(receipt.Request, request)
            || !Enum.IsDefined(receipt.Outcome) || !Enum.IsDefined(receipt.Reason)
            || receipt.Outcome == ModelTurnOutcome.Succeeded
                && (receipt.DurableState != HostTaskState.Succeeded || receipt.Reason != LocalContextTurnReason.Completed))
        {
            return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.InvalidResponse);
        }
        var result = binding is not null && receipt.ModelResult is { } modelResult && modelResult.Outcome == receipt.Outcome
            ? binding.Resolve(modelResult)
            : LocalFileAnswerResult.Empty(receipt.Outcome switch
            {
                ModelTurnOutcome.Succeeded => LocalFileAnswerOutcome.InvalidResponse,
                ModelTurnOutcome.Cancelled => LocalFileAnswerOutcome.Cancelled,
                ModelTurnOutcome.Denied or ModelTurnOutcome.DeniedEgress => LocalFileAnswerOutcome.Denied,
                _ => LocalFileAnswerOutcome.Unavailable,
            });
        return result with { ExecutionReason = receipt.Reason, ExecutionState = receipt.DurableState };
    }
}
