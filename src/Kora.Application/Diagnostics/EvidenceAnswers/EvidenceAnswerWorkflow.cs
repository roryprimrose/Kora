using Kora.Core.Auditing;
using Kora.Core.Context.EvidenceAnswers;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Diagnostics.EvidenceAnswers;

public sealed partial class EvidenceAnswerWorkflow(IEvidenceAnswerSourceUsePolicy sourceUse,
    IEvidenceAnswerConversation conversation, ISessionWorkspaceAccess access, ISecurityAuditLog audit,
    TimeProvider time, ILogger<EvidenceAnswerWorkflow> logger)
{
    public async Task<EvidenceAnswerResult> AnswerAsync(EvidenceAnswerReview review,
        Func<bool> admitted, CancellationToken token)
    {
        var input = HostActivity.Current;
        if (input is null || input.Activity!.IsStopped || input.Outcome != HostOperationOutcome.Unknown
            || !ReferenceEquals(input.Request, review.Original) || !review.TryConsume())
        { return new(EvidenceAnswerOutcome.Denied); }
        var control = access.ControlRevision;
        bool Current() => access.CanControl && access.CanInspect && access.ControlRevision == control
            && admitted() && !token.IsCancellationRequested && time.GetUtcNow() < review.ExpiresAt
            && !input.Activity!.IsStopped && input.Outcome == HostOperationOutcome.Unknown;
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Request);
        var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
            "evidence.answer.local", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser,
            "evidence.answer.exact-page");
        using var auditActivity = HostActivity.BeginAudit(input.Request, requested);
        var outcome = EvidenceAnswerOutcome.Unavailable;
        var started = false;
        try
        {
            audit.Write(requested);
            async Task Validate(CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!Current()) { throw new EvidenceUseClosedException(EvidenceAnswerOutcome.Denied); }
                var decision = await sourceUse.AdmitAsync(review, cancellation).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                if (!Current()) { throw new EvidenceUseClosedException(EvidenceAnswerOutcome.Denied); }
                if (decision != EvidenceSourceUseDecision.Allowed)
                {
                    throw new EvidenceUseClosedException(decision == EvidenceSourceUseDecision.Unavailable
                        ? EvidenceAnswerOutcome.Unavailable : EvidenceAnswerOutcome.Denied);
                }
            }
            await Validate(token).ConfigureAwait(false);
            started = true;
            var receipt = await conversation.SubmitAsync(review, Current, Validate, token).ConfigureAwait(false);
            await Validate(token).ConfigureAwait(false);
            receipt.Message.Validate();
            if (receipt.Message.Request.SessionId != review.Destination.SessionId
                || !string.Equals(receipt.Message.Body, review.Question, StringComparison.Ordinal)
                || receipt.Task.Request != receipt.Message.Request || !receipt.Task.IsTerminal
                || receipt.Task.State != receipt.Message.ExpectedTaskState)
            { throw new InvalidOperationException("The exact durable answer receipt is unconfirmed; do not retry."); }
            outcome = receipt.Message.Outcome switch
            {
                ModelTurnOutcome.Succeeded when receipt.Message.AssistantResponse is not null =>
                    string.Equals(receipt.Message.AssistantResponse.Body, EvidenceAnswerResponse.Unsupported, StringComparison.Ordinal)
                        ? EvidenceAnswerOutcome.Unsupported : EvidenceAnswerOutcome.Succeeded,
                ModelTurnOutcome.Cancelled => EvidenceAnswerOutcome.Cancelled,
                ModelTurnOutcome.Unknown => EvidenceAnswerOutcome.InvalidResponse,
                ModelTurnOutcome.Denied or ModelTurnOutcome.DeniedEgress => EvidenceAnswerOutcome.Denied,
                _ => EvidenceAnswerOutcome.Unavailable,
            };
            return new(outcome, receipt);
        }
        catch (EvidenceUseClosedException exception) { outcome = exception.Outcome; return new(outcome); }
        catch (OperationCanceledException) { outcome = EvidenceAnswerOutcome.Cancelled; return new(outcome); }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            Failure(logger, exception.GetType().Name);
            return new(outcome);
        }
        finally
        {
            if (started) { await conversation.JoinAsync().ConfigureAwait(false); }
            var succeeded = outcome is EvidenceAnswerOutcome.Succeeded or EvidenceAnswerOutcome.Unsupported;
            audit.Write(requested.WithOutcome(succeeded ? SecurityAuditOutcome.Succeeded
                : outcome == EvidenceAnswerOutcome.Cancelled ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Failed));
            var terminal = succeeded ? HostOperationOutcome.Completed : outcome == EvidenceAnswerOutcome.Cancelled
                ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed;
            auditActivity.Complete(terminal);
            activity.Complete(terminal);
        }
    }

    private sealed class EvidenceUseClosedException(EvidenceAnswerOutcome outcome) : InvalidOperationException
    {
        internal EvidenceAnswerOutcome Outcome { get; } = outcome;
    }
}
