using Kora.Application.Hosting.ModelHistoryContext;
using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private ModelHistoryContextSource? modelHistorySource;
    private long modelHistoryContextEpoch;
    public bool CanOpenModelHistoryContext => CanCompose && !assistantReply
        && SelectedAnswerRecord?.AssistantResponse is { } answer && ModelHistoryContextProposal.IsAdvisory(answer.Body);
    public string ModelHistoryContextQuestion => modelHistorySource is { IsCurrent: true } source
        ? "Unchanged original USER request (" + source.Origin + ") - LOCAL ONLY, NO TOOLS\n" + source.OriginalQuestion
        : string.Empty;
    internal bool HasModelHistoryContext => historyContextOpen && modelHistorySource?.IsCurrent == true;

    internal Task OpenModelHistoryContextAsync(Func<bool>? presenterAdmission = null) =>
        RunConversationAsync(async (target, live) =>
        {
            ClearHistoryContext();
            var message = SelectedAnswerRecord;
            if (assistantReply || !access.CanControl || conversationSourceHeld
                || message?.AssistantResponse is null
                || !ConversationMessages.Any(row => ReferenceEquals(row, message)))
            { throw new InvalidOperationException("Select the exact retained model history advisory in this Active conversation."); }
            var page = conversationPage;
            var completed = completedConversationMessage;
            var epoch = modelHistoryContextEpoch;
            var buffer = composerBufferRevision;
            var selection = answerSelectionRevision;
            bool Original() => live() && access.CanControl && !assistantReply && !conversationSourceHeld
                && modelHistoryContextEpoch == epoch && composerBufferRevision == buffer
                && answerSelectionRevision == selection && ReferenceEquals(SelectedAnswerRecord, message)
                && ReferenceEquals(page, conversationPage) && ReferenceEquals(completed, completedConversationMessage)
                && presenterAdmission?.Invoke() != false;
            var source = await conversation!.BindModelHistoryContextAsync(target, message, Original, lifetime.Token);
            if (!Original()) { source.Dispose(); throw new OperationCanceledException("The exact advisory selection changed."); }
            modelHistorySource = source;
            historyContextOpen = true;
            historyContextControl = access.ControlRevision;
            historyContextSelectionEpoch = selectionEpoch;
            historyContextCandidates = ConversationMessages
                .Where(row => row.Outcome is not null && row.Sequence < message.Sequence)
                .SelectMany(row => row.AssistantResponse is null
                    ? new[] { new HistoryContextCandidate(new(row, ConversationHistoryRole.User)) }
                    : new[] { new HistoryContextCandidate(new(row, ConversationHistoryRole.User)),
                        new HistoryContextCandidate(new(row, ConversationHistoryRole.Assistant)) }).ToArray();
            conversationStatus = "Select only the earlier whole messages you want to disclose. Nothing preselected or submitted. "
                + "No extra history was read. The original question and channel are fixed; your unsent composer is unchanged.";
        });

    internal void RefreshModelHistoryContextAvailability() => NotifyHistoryContext();

    internal void ReportModelHistoryContextFailure(Exception exception)
    {
        Failure(logger, exception.GetType().Name);
        ClearHistoryContext();
        conversationStatus = "Model history review unavailable or unconfirmed. Unsent input unchanged; no retry or successful answer inferred. Inspect exact receipts.";
        NotifyConversation();
    }
}
