using System.Diagnostics;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    public Task<SessionAttachmentAnswerReview> PrepareAttachmentAnswerAsync(SessionWorkspaceEntry target,
        SessionFileAttachment expected, string question, Func<bool> admitted, CancellationToken token,
        RequestOrigin origin = RequestOrigin.LocalUi) =>
        (attachmentAnswers ?? throw new InvalidOperationException("Retained attachment answering is unavailable."))
            .Prepare(target, expected, question, admitted, token, origin);

    public async Task<ConversationSubmissionReceipt> SubmitAttachmentAnswerAsync(SessionAttachmentAnswerReview review,
        Func<bool> admitted, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(review);
        var workflow = attachmentAnswers
            ?? throw new InvalidOperationException("Retained attachment answering is unavailable.");
        var request = new HostRequest(new(Guid.NewGuid()), review.Target.Authority.SessionId,
            new(Guid.NewGuid()), review.Origin);
        var submissionEntered = false;
        try
        {
            return await workflow.Submit(review,
                (context, live, validate, resolve, cancellation) =>
                {
                    submissionEntered = true;
                    return SubmitAttachmentCore(review.Target, context, live, validate, resolve, cancellation);
                }, request, admitted, token).ConfigureAwait(false);
        }
        catch (Exception exception) when (submissionEntered
            && exception is not (OutOfMemoryException or ConversationSubmissionUnconfirmedException or ConversationCapacityException))
        {
            throw new ConversationSubmissionUnconfirmedException(request, exception);
        }
    }

    private async Task<ConversationSubmissionReceipt> SubmitAttachmentCore(SessionWorkspaceEntry target,
        ModelContextEnvelope context, Func<bool> admitted, Func<CancellationToken, Task> validateSource,
        Func<ModelTurnResult, ModelTurnResult> resolveResponse, CancellationToken token)
    {
        var request = context.Request;
        RequireOriginalSource(request.Origin);
        ConversationText.Validate(context.UserRequest, submission: true);
        context.RequireCompleteInputLimit();
        var control = access.ControlRevision;
        bool Live() => access.CanControl && access.CanInspect && access.ControlRevision == control && admitted();
        if (!Live() || !turns.IsQuiescent || context.ToolMode != ModelToolMode.None
            || context.Evidence.Length == 0 || context.Evidence.Any(item => item.Disclosure != ModelEvidenceDisclosure.LocalOnly)
            || target.Authority.SessionId != request.SessionId)
        { throw new InvalidOperationException("Attachment answering requires exact private, local-only, tool-free admission."); }
        await validateSource(token).ConfigureAwait(false);
        var actual = await store.ReadMetadataAsync(request.SessionId, token).ConfigureAwait(false);
        if (actual != target || !actual.Authority.IsActive || !Live())
        { throw new InvalidOperationException("The exact attachment conversation changed before retention."); }
        ActivityLink[] links = HostActivity.Current?.Activity is { } cause ? [new(cause.Context)] : [];
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request, links);
        try
        {
            var retained = await conversations.RetainConversationMessageAsync(request, actual.Authority.Generation,
                actual.Metadata?.Revision.Value ?? 0, context.UserRequest, Live, token).ConfigureAwait(false);
            retained.Message.Validate();
            if (retained.Message.Outcome is not null || retained.Message.Request != request
                || retained.Message.Generation != actual.Authority.Generation || retained.Message.Revision != 1
                || !string.Equals(retained.Message.Body, context.UserRequest, StringComparison.Ordinal)
                || retained.Task.Request != request || retained.Task.State != HostTaskState.IntentRecorded
                || retained.Task.Revision.Value != 1 || !Live())
            { throw new InvalidDataException("The original attachment question retention is unconfirmed."); }
            await validateSource(token).ConfigureAwait(false);
            if (!Live() || !turns.IsQuiescent)
            { throw new InvalidOperationException("Source or host admission closed before attachment turn admission."); }
            var admission = await turns.AdmitAsync(ModelTurnChoice.Default, context, null, token).ConfigureAwait(false);
            ConversationSubmissionReceipt receipt;
            if (admission.Turn is not null)
            {
                receipt = await ExecuteRetainedAsync(actual, retained, admission, context, Live,
                    validateSource, token, resolveResponse).ConfigureAwait(false);
            }
            else
            {
                var result = admission.Result.Response is not null || admission.Result.Outcome == ModelTurnOutcome.Succeeded
                    ? new ModelTurnResult(ModelTurnOutcome.Denied, ModelTurnReason.InvalidProviderResponse) : admission.Result;
                await validateSource(token).ConfigureAwait(false);
                receipt = await conversations.CompleteConversationSubmissionAsync(request, result.Outcome,
                    result.Reason, Live, token).ConfigureAwait(false);
                if (receipt.Message.Outcome != result.Outcome || receipt.Message.Reason != result.Reason)
                { throw new IOException("Attachment terminal retention is unconfirmed; inspect without retry."); }
            }
            await RequireSubmissionReadbackAsync(receipt, actual, request, context.UserRequest, Live,
                validateSource, token).ConfigureAwait(false);
            activity.Complete(receipt.Message.Outcome == ModelTurnOutcome.Succeeded ? HostOperationOutcome.Completed
                : receipt.Message.Outcome == ModelTurnOutcome.Cancelled ? HostOperationOutcome.Cancelled
                : receipt.Message.Outcome == ModelTurnOutcome.Unknown ? HostOperationOutcome.Unknown : HostOperationOutcome.Failed);
            return receipt;
        }
        catch (OperationCanceledException exception)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw new ConversationSubmissionUnconfirmedException(request, exception);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure(logger, exception.GetType().Name);
            activity.Complete(HostOperationOutcome.Unknown);
            if (exception is ConversationCapacityException) { throw; }
            throw new ConversationSubmissionUnconfirmedException(request, exception);
        }
    }
}
