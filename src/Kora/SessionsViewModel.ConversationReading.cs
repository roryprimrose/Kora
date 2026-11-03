using Kora.Core.Conversation;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private bool conversationUpdates;
    private long conversationTailRevision;
    private long conversationReadingControl;
    internal long ConversationReaderControl => access.ControlRevision;
    private bool ConversationReadingAvailable => CanPresentDetails && conversationReadingControl == access.ControlRevision
        && (conversationPage is null || conversationPage.Session == selected?.Authority)
        && (completedConversationMessage is null || completedConversationMessage.Request.SessionId == selected?.Authority.SessionId
            && completedConversationMessage.Generation == selected.Authority.Generation);

    public bool CanPresentConversationReader => CanPresentDetails && selected is not null && conversation is not null;
    public bool HasConversationUpdates => ConversationReadingAvailable && conversationUpdates;
    public string ConversationUpdatesLabel => HasConversationUpdates ? "New updates - read latest" : "Read latest messages";
    public IReadOnlyList<ConversationMessage> ConversationUpdates =>
        ConversationReadingAvailable && conversationPage is { Messages.Length: > 0 } ? completionMessages : [];
    internal IEnumerable<ConversationMessage> ObservedConversationMessages => ConversationMessages.Concat(ConversationUpdates);
    internal long ConversationTailRevision => conversationTailRevision;

    private void PublishConversationCompletion(ConversationMessage message)
    {
        message.Validate();
        if (!CanPresentDetails || selected?.Authority.SessionId != message.Request.SessionId
            || selected.Authority.Generation != message.Generation)
        { throw new InvalidOperationException("The completion does not belong to the current exact private conversation."); }
        if (!ConversationReadingAvailable) { conversationPage = null; completedConversationMessage = null; }
        if (completedConversationMessage is null || message.Sequence >= completedConversationMessage.Sequence)
        { completedConversationMessage = message; }
        conversationReadingControl = access.ControlRevision;
        conversationUpdates = true;
    }

    public Task ReadLatestConversationAsync(Func<bool>? admitted = null) => RunConversationAsync(async (target, live) =>
    {
        void RequirePresentation()
        {
            if (!live() || admitted?.Invoke() == false)
            { throw new OperationCanceledException("The original conversation reader was retired. Read again explicitly."); }
        }
        RequirePresentation();
        var page = await conversation!.ReadPageAsync(target.Authority.SessionId, null, 25, lifetime.Token);
        RequirePresentation();
        while (page.Next is { } cursor)
        {
            if (page.Session != target.Authority || cursor.After <= 0 || cursor.After >= cursor.Ceiling
                || cursor.Ceiling > ConversationText.MaximumSessionMessages)
            { throw new InvalidDataException("The exact bounded conversation continuation is invalid."); }
            var next = await conversation.ReadPageAsync(target.Authority.SessionId, cursor, 25, lifetime.Token);
            RequirePresentation();
            if (next.Session != page.Session || next.Ceiling != page.Ceiling || next.HeadRevision != page.HeadRevision
                || next.Messages.IsEmpty || next.Messages[0].Sequence <= cursor.After
                || next.Next is { } following && following.After <= cursor.After)
            { throw new InvalidDataException("The conversation snapshot changed or made no bounded progress. Read again explicitly."); }
            page = next;
        }
        if (page.Session != target.Authority)
        { throw new OperationCanceledException("The exact selected conversation generation changed."); }
        conversationPage = page;
        conversationReadingControl = access.ControlRevision;
        completedConversationMessage = null;
        conversationUpdates = false;
        conversationSourceHeld = false;
        composerAuthority = target.Authority;
        conversationTailRevision++;
        RememberUnsent();
        conversationStatus = "Fresh latest bounded exact-session page admitted. Earlier pages are not accumulated. "
            + "Unsent input is unchanged; reading does not submit, resume or mark answers reviewed."
            + (conversationHeld ? " Submission uncertainty remains held; inspect exact task receipts." : string.Empty);
    }, sourceRead: true);

    private void NotifyConversationReading()
    {
        OnPropertyChanged(nameof(HasConversationUpdates));
        OnPropertyChanged(nameof(CanPresentConversationReader));
        OnPropertyChanged(nameof(ConversationUpdatesLabel));
        OnPropertyChanged(nameof(ConversationUpdates));
        OnPropertyChanged(nameof(ConversationTailRevision));
    }
}
