using Kora.Core.Auditing;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Microsoft.Extensions.Logging;

namespace Kora.Tools.Files;

public sealed partial class LocalFileAnswer(LocalFilePreview preview, ILocalFileRetrieval retrieval,
    ILocalFileModelUsePolicy sourceUse, ILocalFileAnswerModel model, ISecurityAuditLog audit,
    TimeProvider time, ILogger<LocalFileAnswer> logger)
{
    public async Task<LocalFileAnswerResult> ExecuteAsync(LocalFileReference exactSource, string question,
        Func<bool> canPresent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exactSource);
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(canPresent);
        var context = HostActivity.RequireCurrent();
        if (context.Outcome != HostOperationOutcome.Unknown || context.Request.InvocationId is not null)
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
                var decision = await sourceUse.AdmitAsync(source, question, search, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (!current()) { return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Stale); }
                if (decision != LocalFileModelUseDecision.Allowed)
                {
                    return LocalFileAnswerResult.Empty(decision == LocalFileModelUseDecision.Unavailable
                        ? LocalFileAnswerOutcome.SourceUseUnavailable : LocalFileAnswerOutcome.Denied);
                }
                LocalFileAnswerContext binding;
                try { binding = new(source, search, question, request, new(generation), time.GetUtcNow()); }
                catch (InvalidDataException)
                {
                    return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Denied);
                }
                if (binding.Envelope.Serialize().Length > ModelContextEnvelope.MaximumInputUtf8Bytes)
                {
                    return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.ContextLimit);
                }
                token.ThrowIfCancellationRequested();
                if (!current()) { return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Stale); }
                var reply = await model.RunAsync(binding.Envelope, current, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (!current()) { return LocalFileAnswerResult.Empty(LocalFileAnswerOutcome.Stale); }
                foreach (var citation in search.Citations)
                {
                    _ = LocalFileLexicalRetrieval.ResolveCitation(source, citation, search.Mode);
                }
                return binding.Resolve(reply, search);
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
}
