using Kora.Core.Auditing;
using Kora.Core.Configuration.SettingInspection;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration.SettingInspection;

public sealed partial class SettingInspectionWorkflow(ConfirmedSettingsSource source,
    ISettingInspectionConversation conversation, ISessionWorkspaceAccess access, ISecurityAuditLog audit,
    TimeProvider time, ILogger<SettingInspectionWorkflow> logger)
{
    public async Task<SettingInspectionSession> ReviewAsync(string question, Func<bool> admitted, CancellationToken token)
    {
        var original = HostActivity.RequireCurrent();
        Application.Hosting.SessionConversationService.RequireOriginalSource(original.Request.Origin);
        SettingInspectionReview.ValidateQuestion(question, original.Request);
        var now = time.GetUtcNow();
        var control = access.ControlRevision;
        bool Current() => access.CanControl && access.CanInspect && access.ControlRevision == control && admitted()
            && !token.IsCancellationRequested;
        if (!Current()) { throw new InvalidOperationException("Original private host admission is unavailable."); }
        var snapshot = await source.CaptureAsync(token).ConfigureAwait(false);
        if (!Current()) { throw new OperationCanceledException("Original input changed during confirmed observation."); }
        var review = new SettingInspectionReview(snapshot.Observations, question, original.Request, now);
        if (time.GetUtcNow() >= review.ExpiresAt)
        { throw new InvalidOperationException("The original confirmed observation expired before review."); }
        return new(review, snapshot, () => Current() && source.IsCurrent(snapshot) && time.GetUtcNow() < review.ExpiresAt);
    }

    public async Task<SettingInspectionResult> AnswerAsync(SettingInspectionSession session, Func<bool> admitted, CancellationToken token)
    {
        var review = session.Review;
        var original = HostActivity.Current;
        if (original is null || original.Activity!.IsStopped || original.Outcome != HostOperationOutcome.Unknown
            || !ReferenceEquals(original.Request, review.Original) || !review.TryConsume())
        { return new(SettingInspectionOutcome.Denied); }
        bool Current() => session.Current() && admitted() && !token.IsCancellationRequested
            && !original.Activity!.IsStopped && original.Outcome == HostOperationOutcome.Unknown;
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Request);
        var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
            "setting.inspection.local", SecurityAuditOutcome.Requested,
            review.Original.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
            "settings.confirmed-scalars");
        using var auditActivity = HostActivity.BeginAudit(original.Request, requested);
        var outcome = SettingInspectionOutcome.Unconfirmed;
        try
        {
            audit.Write(requested);
            async Task Validate(CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!Current()) { throw new InvalidOperationException("Original setting inspection admission changed."); }
                await source.ValidateAsync(session.Snapshot, cancellation).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                if (!Current()) { throw new InvalidOperationException("Setting inspection admission changed during validation."); }
            }
            await Validate(token).ConfigureAwait(false);
            ConversationSubmissionReceipt receipt;
            try { receipt = await conversation.SubmitAsync(review, Current, Validate, token).ConfigureAwait(false); }
            finally { await conversation.JoinAsync().ConfigureAwait(false); }
            await Validate(token).ConfigureAwait(false);
            receipt.Message.Validate();
            if (receipt.Message.Request.SessionId != review.Destination.SessionId
                || !string.Equals(receipt.Message.Body, review.Question, StringComparison.Ordinal)
                || receipt.Task.Request != receipt.Message.Request || !receipt.Task.IsTerminal
                || receipt.Task.State != receipt.Message.ExpectedTaskState)
            { throw new IOException("The exact conversation receipt is unconfirmed; inspect Sessions without retry."); }
            outcome = receipt.Message.Outcome switch
            {
                ModelTurnOutcome.Succeeded when receipt.Message.AssistantResponse is not null =>
                    string.Equals(receipt.Message.AssistantResponse.Body, SettingInspectionReview.Unsupported, StringComparison.Ordinal)
                        ? SettingInspectionOutcome.Unsupported : SettingInspectionOutcome.Succeeded,
                ModelTurnOutcome.Cancelled => SettingInspectionOutcome.Cancelled,
                ModelTurnOutcome.Unknown => SettingInspectionOutcome.InvalidResponse,
                ModelTurnOutcome.Denied or ModelTurnOutcome.DeniedEgress => SettingInspectionOutcome.Denied,
                _ => SettingInspectionOutcome.Unavailable,
            };
            return new(outcome, receipt);
        }
        catch (OperationCanceledException) { outcome = SettingInspectionOutcome.Cancelled; return new(outcome); }
        catch (InvalidDataException) { outcome = SettingInspectionOutcome.Unavailable; return new(outcome); }
        catch (InvalidOperationException) { outcome = SettingInspectionOutcome.Denied; return new(outcome); }
        catch (IOException exception)
        {
            Failure(logger, exception.GetType().Name);
            return new(outcome);
        }
        finally
        {
            var succeeded = outcome is SettingInspectionOutcome.Succeeded or SettingInspectionOutcome.Unsupported;
            audit.Write(requested.WithOutcome(succeeded ? SecurityAuditOutcome.Succeeded
                : outcome == SettingInspectionOutcome.Cancelled ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Failed));
            var terminal = succeeded ? HostOperationOutcome.Completed : outcome == SettingInspectionOutcome.Cancelled
                ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed;
            auditActivity.Complete(terminal);
            activity.Complete(terminal);
        }
    }
}
