using Kora.Core.Auditing;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Microsoft.Extensions.Logging;

namespace Kora.Tools.Files;

public sealed partial class LocalFileAnswer(LocalFilePreview preview, ILocalFileRetrieval retrieval,
    ILocalFileModelUsePolicy sourceUse, IModelContextRunner model, ISecurityAuditLog audit,
    TimeProvider time, ILogger<LocalFileAnswer> logger)
{
    public async Task<LocalFileAnswerResult> ExecuteAsync(LocalFileReference exactSource, string question,
        LocalContextSessionTarget reviewedTarget, Func<bool> canPresent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exactSource);
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(reviewedTarget);
        ArgumentNullException.ThrowIfNull(canPresent);
        var context = HostActivity.RequireCurrent();
        if (context.Outcome != HostOperationOutcome.Unknown || context.Request.InvocationId is not null
            || !ReferenceEquals(reviewedTarget.Request, context.Request))
        {
            return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Denied);
        }
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Tool);
        var request = context.Request;
        var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
            "file.answer.local", SecurityAuditOutcome.Requested,
            request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
            "file.answer.selected-revision");
        LocalFileAnswerResult result;
        try
        {
            audit.Write(requested);
            void Commit(LocalFileAnswerResult outcome) => audit.Write(requested.WithOutcome(
                outcome.Outcome is LocalFileAnswerOutcome.Succeeded or LocalFileAnswerOutcome.Unsupported or LocalFileAnswerOutcome.NoEvidence
                    ? SecurityAuditOutcome.Succeeded : outcome.Outcome == LocalFileAnswerOutcome.Cancelled
                        ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Failed, outcome.Outcome.ToString().ToLowerInvariant()));
            result = await preview.AnswerAsync(exactSource, canPresent, async (source, generation, current, token) =>
            {
                if (!LocalFileRetrievalPolicy.TryQuery(question, out _))
                {
                    return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.InvalidQuestion);
                }
                var search = await Task.Run(() => retrieval.Search(source, exactSource, question,
                    LocalFileRetrievalMode.LexicalLines, time.GetUtcNow(), token), token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (!current()) { return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Stale); }
                if (search.Mode != LocalFileRetrievalMode.LexicalLines
                    || !string.Equals(search.PolicyVersion, LocalFileRetrievalPolicy.Version, StringComparison.Ordinal))
                {
                    return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Denied);
                }
                if (search.Outcome != LocalFileSearchOutcome.Matched)
                {
                    return LocalFileAnswerResult.Empty(search.Outcome == LocalFileSearchOutcome.NoMatch
                        ? LocalFileAnswerOutcome.NoEvidence : LocalFileAnswerOutcome.Denied);
                }
                LocalFileModelUseDecision decision;
                try { decision = await sourceUse.AdmitAsync(source, question, search, token).ConfigureAwait(false); }
                catch (Exception exception) when (exception is IOException or InvalidOperationException)
                {
                    Failure(logger, exception.GetType().Name);
                    return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Unavailable);
                }
                token.ThrowIfCancellationRequested();
                if (!current()) { return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Stale); }
                if (decision != LocalFileModelUseDecision.Allowed)
                {
                    return LocalFileAnswerResult.Empty(decision is LocalFileModelUseDecision.Unavailable
                        or LocalFileModelUseDecision.SourceUsePolicyMissing
                        ? LocalFileAnswerOutcome.SourceUseUnavailable : LocalFileAnswerOutcome.Denied) with
                    { SourceUseDecision = decision };
                }
                var admission = await model.AdmitAsync(reviewedTarget, current, token).ConfigureAwait(false);
                if (admission.Turn is not { } admitted)
                {
                    var rejection = admission.Rejection
                        ?? throw new InvalidOperationException("Model context admission returned neither a lease nor a rejection.");
                    return ResolveReceipt(request, null, rejection, search);
                }
                await using var turn = admitted;
                token.ThrowIfCancellationRequested();
                if (!current()) { return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Stale); }
                if (!ReferenceEquals(turn.Request, request) || turn.SessionGeneration.Value <= 0)
                {
                    return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Denied);
                }
                [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003",
                    Justification = "Reads this operation's owned lease completion after asynchronous disposal has awaited terminal quiescence; no UI join.")]
                async Task<LocalFileAnswerResult> Abandon(LocalFileAnswerOutcome outcome)
                {
                    await turn.DisposeAsync().ConfigureAwait(false);
                    var terminal = await turn.Completion.ConfigureAwait(false);
                    return ReferenceEquals(terminal.Request, request)
                        ? LocalFileAnswerResult.Empty(outcome) with
                        { ExecutionReason = terminal.Reason, ExecutionState = terminal.DurableState }
                        : LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.InvalidResponse);
                }
                LocalFileAnswerContext binding;
                try { binding = new(source, search, question, turn.Request, new(generation), time.GetUtcNow()); }
                catch (InvalidDataException)
                {
                    return await Abandon(LocalFileAnswerOutcome.Denied).ConfigureAwait(false);
                }
                if (binding.Envelope.Serialize().Length > ModelContextEnvelope.MaximumInputUtf8Bytes)
                {
                    return await Abandon(LocalFileAnswerOutcome.ContextLimit).ConfigureAwait(false);
                }
                token.ThrowIfCancellationRequested();
                if (!current()) { return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Stale); }
                var receipt = await turn.RunAsync(binding.Envelope, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (!current()) { return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Stale); }
                foreach (var citation in search.Citations)
                {
                    _ = LocalFileLexicalRetrieval.ResolveCitation(source, citation, search.Mode);
                }
                return ResolveReceipt(turn.Request, binding, receipt, search);
            }, Commit, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            Failure(logger, exception.GetType().Name);
            result = LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Unavailable);
        }
        Report(logger, result.Outcome, result.Claims.Length);
        activity.Complete(result.Outcome is LocalFileAnswerOutcome.Succeeded or LocalFileAnswerOutcome.Unsupported or LocalFileAnswerOutcome.NoEvidence
            ? HostOperationOutcome.Completed : result.Outcome == LocalFileAnswerOutcome.Cancelled
                ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        return result;
    }

    private static LocalFileAnswerResult ResolveReceipt(HostRequest request, LocalFileAnswerContext? binding,
        LocalContextTurnReceipt receipt, LocalFileSearchResult search)
    {
        if (!ReferenceEquals(receipt.Request, request)
            || !Enum.IsDefined(receipt.Outcome) || !Enum.IsDefined(receipt.Reason)
            || receipt.Outcome == ModelTurnOutcome.Succeeded
                && (receipt.DurableState != HostTaskState.Succeeded || receipt.Reason != LocalContextTurnReason.Completed))
        {
            return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.InvalidResponse);
        }
        LocalFileAnswerResult result;
        if (binding is not null && receipt.ModelResult is { } reply && reply.Outcome == receipt.Outcome)
        {
            result = binding.Resolve(reply, search);
        }
        else
        {
            result = LocalFileAnswerResult.Empty(receipt.Outcome switch
            {
                ModelTurnOutcome.Succeeded => LocalFileAnswerOutcome.InvalidResponse,
                ModelTurnOutcome.Cancelled => LocalFileAnswerOutcome.Cancelled,
                ModelTurnOutcome.Denied or ModelTurnOutcome.DeniedEgress => LocalFileAnswerOutcome.Denied,
                _ => LocalFileAnswerOutcome.Unavailable,
            });
        }
        return result with { ExecutionReason = receipt.Reason, ExecutionState = receipt.DurableState };
    }
}
