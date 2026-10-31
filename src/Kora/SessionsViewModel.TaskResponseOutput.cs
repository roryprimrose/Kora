using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private SessionRequestCommandService? taskOutputCommands;
    private SessionRequestReview? taskOutputReview;
    private TaskResponseMode taskOutputMode;
    private long taskOutputEpoch;
    public IReadOnlyList<TaskResponseMode> TaskOutputModes { get; } = Enum.GetValues<TaskResponseMode>();
    public TaskResponseMode TaskOutputMode
    {
        get => taskOutputMode;
        set
        {
            if (!Enum.IsDefined(value)) { throw new ArgumentOutOfRangeException(nameof(value)); }
            CancelTaskOutputReview();
            taskOutputMode = value;
            NotifyTaskOutput();
        }
    }
    public bool CanReviewTaskOutput => CanSubmitConversation && !assistantReply;
    public bool CanConfirmTaskOutput => CanCompose && taskOutputReview is not null;
    public string TaskOutputReviewText => !closed && access.CanInspect && taskOutputReview is { } review
        ? "This task only\nConversation: " + (review.Target.Metadata?.Name.Value ?? "Unnamed conversation")
            + "\nOriginal channel: " + review.Origin + "\nDeclared output: " + review.TaskResponseMode
            + "\nConfigured output: " + review.ConfiguredTaskResponseMode
            + "\nEffective output remains subject to live privacy/call/output policy. VoiceOnly cannot hide required visuals."
            + "\nComplete unchanged input:\n" + review.Body
            + "\nConfirm separately once or cancel. Session/app preferences and later tasks are unchanged."
        : "Choose output for one new task. Review the exact Active conversation and complete input, then confirm separately. Choosing alone never submits, speaks, listens or changes a preference.";

    public Task ReviewTaskOutputAsync() => RunConversationAsync(async (target, live) =>
    {
        CancelTaskOutputReview();
        var proposal = taskOutputEpoch;
        var buffer = composerBufferRevision;
        var submitting = false;
        taskOutputSubmissionStarted = () => submitting = true;
        bool SourceLive() => live() && access.CanControl
            && (submitting || proposal == taskOutputEpoch && buffer == composerBufferRevision);
        taskOutputCommands ??= conversation!.CreateRequestCommands();
        var result = await taskOutputCommands.ExecuteAsync(new(SessionCommandOperation.RequestReview,
            target.Authority.SessionId.Value, target.Authority.Generation.Value, target.Metadata?.Revision.Value ?? 0)
        { RequestBody = composerBody, RequestTaskResponseMode = taskOutputMode },
            RequestOrigin.LocalUi, SourceLive, lifetime.Token);
        if (!SourceLive()) { throw new OperationCanceledException("The original input, output choice or conversation changed."); }
        taskOutputReview = result.RequestReview;
        conversationStatus = "Review this one task's exact input, destination and output. Nothing submitted or spoken.";
    });

    private Action? taskOutputSubmissionStarted;
    internal Func<Task> CaptureTaskOutputDecision(bool confirm)
    {
        var expected = taskOutputReview;
        return () => confirm ? ConfirmTaskOutputAsync(expected) : CancelExactTaskOutput(expected);
    }

    private Task CancelExactTaskOutput(SessionRequestReview? expected)
    {
        if (ReferenceEquals(expected, taskOutputReview)) { CancelTaskOutputReview(); }
        return Task.CompletedTask;
    }

    private Task ConfirmTaskOutputAsync(SessionRequestReview? expected) => RunConversationAsync(async (_, _) =>
    {
        var review = taskOutputReview ?? throw new InvalidOperationException("Review this exact new task first.");
        if (!ReferenceEquals(review, expected))
        { throw new InvalidOperationException("The exact one-task review changed; the old control cannot submit."); }
        taskOutputReview = null;
        taskOutputSubmissionStarted!();
        taskOutputSubmissionStarted = null;
        var result = await taskOutputCommands!.ExecuteAsync(new(SessionCommandOperation.RequestConfirm,
            review.Target.Authority.SessionId.Value) { RequestReviewId = review.Id },
            RequestOrigin.LocalUi, () => !closed && access.CanInspect && access.CanControl, lifetime.Token);
        var receipt = result.RequestSubmission!;
        PublishCompletedConversationMessage(receipt.Message);
        if (string.Equals(composerBody, review.Body, StringComparison.Ordinal))
        { composerBody = string.Empty; Interlocked.Increment(ref composerBufferRevision); RememberUnsent(); }
        conversationStatus = TerminalConversationStatus(receipt)
            + " " + result.Message + " Later requests inherit normally.";
    }, submission: true);

    public void CancelTaskOutputReview()
    {
        taskOutputEpoch++;
        taskOutputCommands?.Retire();
        taskOutputReview = null;
        taskOutputSubmissionStarted = null;
        NotifyTaskOutput();
    }

    private void NotifyTaskOutput()
    {
        foreach (var name in new[] { nameof(TaskOutputMode), nameof(TaskOutputReviewText),
            nameof(CanReviewTaskOutput), nameof(CanConfirmTaskOutput) })
        { OnPropertyChanged(name); }
    }
}
