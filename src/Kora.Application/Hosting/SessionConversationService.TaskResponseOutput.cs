using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    private Func<TaskResponseMode, RequestOrigin, TaskResponseOutputReview>? reviewTaskResponseOutput;

    internal void BindTaskResponseOutput(Func<TaskResponseMode, RequestOrigin, TaskResponseOutputReview> review) =>
        reviewTaskResponseOutput = review;

    internal TaskResponseOutputReview ReviewTaskResponseOutput(TaskResponseMode mode, RequestOrigin origin) =>
        (reviewTaskResponseOutput ?? throw new InvalidOperationException(
            "The shared task response-output host is unavailable. Nothing was submitted."))(mode, origin);

    private async Task PresentTaskResponseOutputAsync(PendingConversation pending, ConversationSubmissionReceipt receipt,
        Func<bool> admitted, Func<CancellationToken, Task>? validateSource, CancellationToken token)
    {
        var output = pending.Output;
        if (output is null) { return; }
        using var activity = HostActivity.BeginRoot(receipt.Message.Request,
            HostActivityLayer.Application, HostOperation.Presentation, [pending.Cause]);
        bool Current() => admitted() && access.CanControl && !pending.CancelRequested
            && access.ControlRevision == pending.OutputControlRevision
            && time.GetUtcNow() >= pending.OutputCreatedAt && time.GetUtcNow() < pending.OutputExpiresAt;
        try
        {
            if (Current() && validateSource is not null) { await validateSource(token).ConfigureAwait(false); }
            if (!Current()
                || await store.ReadMetadataAsync(receipt.Message.Request.SessionId, token).ConfigureAwait(false) != pending.Target
                || await conversations.ReadConversationMessageAsync(receipt.Message.Request.SessionId,
                    receipt.Message.Request.RequestId, token).ConfigureAwait(false) != receipt.Message)
            {
                output.Status = "Task output withheld: original authority expired or exact retained source changed. Inspect without replay.";
                activity.Complete(HostOperationOutcome.Failed);
                return;
            }
            await output.PresentAsync(receipt, pending.Target, Current, token).ConfigureAwait(false);
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch { activity.Complete(HostOperationOutcome.Failed); throw; }
    }
}
