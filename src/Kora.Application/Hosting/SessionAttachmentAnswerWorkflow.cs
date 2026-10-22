using Kora.Core.Auditing;
using Kora.Core.Context;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Hosting;

/// <summary>Explicit retained-source review and one-shot model-use consent, never capture or retention renewal.</summary>
public sealed partial class SessionAttachmentAnswerWorkflow(SessionWorkspaceService sessions,
    ISessionWorkspaceAccess access, ILocalFileRetrieval retrieval, ILocalFileModelUsePolicy sourceUse,
    TimeProvider time, ISecurityAuditLog audit, ILogger<SessionAttachmentAnswerWorkflow> logger)
{
    private readonly Guid owner = Guid.NewGuid();

    public async Task<SessionAttachmentAnswerReview> Prepare(SessionWorkspaceEntry target,
        SessionFileAttachment expected, string question, Func<bool> admitted, CancellationToken token,
        RequestOrigin origin = RequestOrigin.LocalUi)
    {
        SessionConversationService.RequireOriginalSource(origin);
        ConversationText.Validate(question, submission: true);
        if (!LocalFileRetrievalPolicy.TryQuery(question, out _))
        { throw new InvalidDataException("The question exceeds the bounded lexical question policy."); }
        var control = access.ControlRevision;
        bool Live() => access.CanControl && access.CanInspect && access.ControlRevision == control && admitted();
        var request = new HostRequest(new(Guid.NewGuid()), target.Authority.SessionId, new(Guid.NewGuid()), origin);
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var requested = Audit(request, "session.attachment.answer.review");
        using var auditActivity = HostActivity.BeginAudit(request, requested);
        try
        {
            audit.Write(requested);
            await RequireSource(target, expected, Live, token).ConfigureAwait(false);
            ConversationText.RequireNoCredentials(expected.File.Text);
            var search = await Task.Run(() => retrieval.Search(expected.File, expected.File.Reference, question,
                time.GetUtcNow(), token), token).ConfigureAwait(false);
            if (search.Outcome != LocalFileSearchOutcome.Matched || search.Truncated)
            { throw new InvalidDataException("No complete bounded lexical evidence is available; nothing was truncated or sent."); }
            // Resolve every span before either disclosure review or model admission.
            var binding = Bind(expected, target, search, question, request, time.GetUtcNow());
            binding.Envelope.RequireCompleteInputLimit();
            await RequireSource(target, expected, Live, token).ConfigureAwait(false);
            audit.Write(requested.WithOutcome(SecurityAuditOutcome.Succeeded));
            auditActivity.Complete(HostOperationOutcome.Completed);
            activity.Complete(HostOperationOutcome.Completed);
            return new(owner, target, expected, search, question, origin, control, time.GetUtcNow().AddMinutes(2), admitted,
                activity.Activity?.Context ?? default);
        }
        catch (OperationCanceledException)
        {
            audit.Write(requested.WithOutcome(SecurityAuditOutcome.Cancelled));
            auditActivity.Complete(HostOperationOutcome.Cancelled);
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure(logger, exception.GetType().Name);
            audit.Write(requested.WithOutcome(SecurityAuditOutcome.Failed));
            auditActivity.Complete(HostOperationOutcome.Failed);
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }

    internal async Task<ConversationSubmissionReceipt> Submit(SessionAttachmentAnswerReview review,
        Func<Func<HostRequest, DateTimeOffset, ModelContextEnvelope>, Func<bool>, Func<CancellationToken, Task>, Func<ModelTurnResult, ModelTurnResult>,
            CancellationToken, Task<ConversationSubmissionReceipt>> submit,
        HostRequest request, Func<bool> admitted, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(review);
        SessionConversationService.RequireOriginalSource(review.Origin);
        if (review.Owner != owner || !review.TryConsume()
            || request.SessionId != review.Target.Authority.SessionId || request.Origin != review.Origin
            || request.InvocationId is not null)
        { throw new InvalidOperationException("Only this host's exact unused attachment review may authorize one turn."); }
        using (review)
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, review.Revocation))
        {
            token = linked.Token;
            var source = review.Source;
            var search = review.Evidence;
            using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request,
                review.Cause == default ? [] : [new(review.Cause)]);
            var requested = Audit(request, "session.attachment.answer.local");
            using var auditActivity = HostActivity.BeginAudit(request, requested);
            ConversationSubmissionReceipt? completed = null;
            bool Live() => !review.IsRevoked && time.GetUtcNow() < review.ExpiresAt
                && access.CanControl && access.CanInspect && access.ControlRevision == review.ControlRevision
                && review.Admitted() && admitted() && !token.IsCancellationRequested;
            async Task Validate(CancellationToken cancellation)
            {
                await RequireSource(review.Target, source, Live, cancellation).ConfigureAwait(false);
                ConversationText.RequireNoCredentials(source.File.Text);
                var decision = await sourceUse.AdmitAsync(source.File, review.Question, search, cancellation).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                if (decision != LocalFileModelUseDecision.Allowed || !Live())
                { throw new SessionAttachmentAnswerUnavailableException(decision); }
            }
            try
            {
                audit.Write(requested);
                await Validate(token).ConfigureAwait(false);
                LocalFileAnswerContext? binding = null;
                ModelContextEnvelope CreateContext(HostRequest original, DateTimeOffset now)
                {
                    if (binding is not null)
                    { throw new InvalidOperationException("The reviewed attachment context is single-use."); }
                    binding = Bind(source, review.Target, search, review.Question, original, now);
                    binding.Envelope.RequireCompleteInputLimit();
                    return binding.Envelope;
                }
                var receipt = await submit(CreateContext, Live, Validate,
                    result => SessionAttachmentAnswerResponse.Resolve(source,
                        binding ?? throw new InvalidOperationException("The original reviewed attachment context was not constructed."),
                        search, result), token).ConfigureAwait(false);
                completed = receipt;
                var outcome = receipt.Message.Outcome == ModelTurnOutcome.Succeeded ? SecurityAuditOutcome.Succeeded
                    : receipt.Message.Outcome == ModelTurnOutcome.Cancelled ? SecurityAuditOutcome.Cancelled
                    : receipt.Message.Outcome == ModelTurnOutcome.Unknown ? SecurityAuditOutcome.Unknown : SecurityAuditOutcome.Failed;
                audit.Write(requested.WithOutcome(outcome));
                await Validate(token).ConfigureAwait(false);
                var terminal = outcome == SecurityAuditOutcome.Succeeded ? HostOperationOutcome.Completed
                    : outcome == SecurityAuditOutcome.Cancelled ? HostOperationOutcome.Cancelled
                    : outcome == SecurityAuditOutcome.Unknown ? HostOperationOutcome.Unknown : HostOperationOutcome.Failed;
                auditActivity.Complete(terminal);
                activity.Complete(terminal);
                return receipt;
            }
            catch (OperationCanceledException)
            {
                audit.Write(requested.WithOutcome(SecurityAuditOutcome.Cancelled));
                auditActivity.Complete(HostOperationOutcome.Cancelled);
                activity.Complete(HostOperationOutcome.Cancelled);
                throw;
            }
            catch (ConversationSubmissionUnconfirmedException exception)
            {
                Failure(logger, exception.GetType().Name);
                auditActivity.Complete(HostOperationOutcome.Unknown);
                activity.Complete(HostOperationOutcome.Unknown);
                try { audit.Write(requested.WithOutcome(SecurityAuditOutcome.Unknown)); }
                catch (Exception auditException) when (auditException is not OutOfMemoryException)
                {
                    Failure(logger, auditException.GetType().Name);
                    throw new ConversationSubmissionUnconfirmedException(exception.Request,
                        new AggregateException(exception, auditException));
                }
                throw;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Failure(logger, exception.GetType().Name);
                audit.Write(requested.WithOutcome(completed is not null ? SecurityAuditOutcome.Unknown : SecurityAuditOutcome.Failed));
                auditActivity.Complete(completed is not null ? HostOperationOutcome.Unknown : HostOperationOutcome.Failed);
                activity.Complete(completed is not null ? HostOperationOutcome.Unknown : HostOperationOutcome.Failed);
                throw;
            }
        }
    }

    private async Task RequireSource(SessionWorkspaceEntry target, SessionFileAttachment expected,
        Func<bool> live, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!live() || !target.Authority.IsActive || expected.Session != target.Authority.SessionId
            || expected.Generation.Value <= 0 || expected.Generation.Value > target.Authority.Generation.Value
            || expected.StorageRevision.Value <= 0)
        { throw new InvalidOperationException("An exact current Active session and retained attachment are required."); }
        var actual = await sessions.ReadAttachmentTarget(target.Authority.SessionId, token).ConfigureAwait(false);
        var retained = await sessions.ReadAttachment(target.Authority.SessionId, token).ConfigureAwait(false);
        var copies = await sessions.PreviewAttachmentRemoval(target.Authority.SessionId, token).ConfigureAwait(false);
        if (actual != target || retained is null || retained.Session != expected.Session
            || retained.Generation != expected.Generation || retained.StorageRevision != expected.StorageRevision
            || retained.File.Reference != expected.File.Reference || retained.File.Review != expected.File.Review
            || !string.Equals(retained.File.Text, expected.File.Text, StringComparison.Ordinal)
            || retained.File.AdmittedAt != expected.File.AdmittedAt
            || copies.Session != expected.Session || copies.Generation != target.Authority.Generation
            || copies.StorageRevision != expected.StorageRevision || copies.File != expected.File.Reference
            || !copies.BodyRetained || copies.ReplacementCopyVerificationPending || copies.ReplacementSwapUnconfirmed || !live())
        { throw new InvalidOperationException("The exact retained source, copy certification or session changed. Inspect again; no alternate source was used."); }
        token.ThrowIfCancellationRequested();
    }

    private static LocalFileAnswerContext Bind(SessionFileAttachment source, SessionWorkspaceEntry target,
        LocalFileSearchResult search, string question, HostRequest request, DateTimeOffset now) =>
        new(source.File, search, question, request, target.Authority.Generation, now,
            new(new(Guid.NewGuid()), source.Session, source.File.Review.Request.TaskId, request.Origin));

    private static SecurityAuditEvent Audit(HostRequest request, string action) =>
        new(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation, action, SecurityAuditOutcome.Requested,
            request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
            "session.attachment.local");
}
