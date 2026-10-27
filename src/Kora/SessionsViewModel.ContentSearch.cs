using Avalonia.Threading;

using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private string contentQuery = string.Empty;
    private IReadOnlyList<ContentMatch> contentMatches = [];
    private ContentSearchPosition? contentPosition;
    private CancellationTokenSource? contentCancellation;
    private long contentEpoch;
    private long contentControl;
    private bool contentSearching;
    private bool contentOpening;
    private bool observingContentSources;
    private ConversationPage? locatedContentPage;
    private string contentSearchStatus = "Search retained messages and answers across conversations. Nothing is sent to a model.";

    public string ContentQuery
    {
        get => !closed && access.CanInspect && contentControl == access.ControlRevision ? contentQuery : string.Empty;
        set
        {
            if (closed || !access.CanInspect) { return; }
            RetireContentSearch();
            contentQuery = value;
            contentControl = access.ControlRevision;
            contentSearchStatus = "Query changed. Search explicitly; text is memory-only and is not composer input.";
            NotifyContentSearch();
        }
    }

    public IReadOnlyList<ContentMatch> ContentMatches =>
        !closed && access.CanInspect && contentControl == access.ControlRevision ? contentMatches : [];
    public string ContentSearchStatus => contentSearchStatus;
    public bool CanSearchContent => CanRead && conversation is not null && !contentSearching && !contentOpening;
    public bool CanNextContentSearch => CanSearchContent && contentControl == access.ControlRevision
        && contentPosition is { Complete: false };
    public bool CanCancelContentSearch => contentSearching || contentOpening;

    public void ClearContentSearch()
    {
        RetireContentSearch();
        contentQuery = string.Empty;
        contentSearchStatus = "Private search text, matches and continuation cleared. Conversation history is unchanged.";
        NotifyContentSearch();
    }

    private void RetireContentSearch()
    {
        contentEpoch++;
        contentCancellation?.Cancel();
        contentMatches = [];
        contentPosition = null;
    }

    public async Task SearchContentAsync(bool next = false)
    {
        if (!CanSearchContent) { return; }
        if (next && !CanNextContentSearch)
        {
            ClearContentSearch();
            contentSearchStatus = "Content search continuation is unavailable or private admission changed. Start a fresh Search.";
            NotifyContentSearch();
            return;
        }
        ObserveContentSources();
        var query = contentQuery;
        var position = next ? contentPosition : new ContentSearchPosition(null, null, false, null, false);
        RetireContentSearch();
        var epoch = contentEpoch;
        var control = access.ControlRevision;
        contentControl = control;
        bool Live() => !closed && access.CanInspect && access.ControlRevision == control && contentEpoch == epoch;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        contentCancellation = cancellation;
        contentSearching = true;
        contentSearchStatus = "Reading bounded retained conversation content. Selection never executes or submits anything.";
        NotifyContentSearch();
        using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Desktop, HostOperation.Request);
        try
        {
            _ = new ConversationSearch(query, ConversationSearchRole.USER);
            if (position is null || position.Complete)
            { throw new ArgumentException("Start a fresh content search.", nameof(next)); }
            var matches = new List<ContentMatch>();
            for (var reads = 0; reads < 8 && matches.Count < 25 && !position.Complete; reads++)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (position.Target is null)
                {
                    var sessionsPage = await service.SearchListAsync(SessionListSearchKind.Browse, SessionListFilter.All,
                        string.Empty, position.AfterSession, 1, cancellation.Token);
                    if (!Live()) { throw new OperationCanceledException("Private content search changed."); }
                    position = sessionsPage.Records.IsEmpty
                        ? position with { Complete = true }
                        : new(sessionsPage.Records.Single(), sessionsPage.Next, false, null, false);
                    continue;
                }
                var target = position.Target;
                var page = await conversation!.SearchAsync(target.Authority.SessionId, query, position.AfterMessage,
                    25 - matches.Count, cancellation.Token,
                    position.Assistant ? ConversationSearchRole.ASSISTANT : ConversationSearchRole.USER);
                if (!Live() || page.Session != target.Authority)
                { throw new OperationCanceledException("The exact conversation search source changed."); }
                matches.AddRange(page.Messages.Select(message =>
                    new ContentMatch(target, message, position.Assistant, page.Ceiling, page.HeadRevision)));
                position = page.Next is not null
                    ? position with { AfterMessage = page.Next }
                    : !position.Assistant ? position with { Assistant = true, AfterMessage = null }
                    : new(null, position.AfterSession, false, null, position.AfterSession is null);
            }
            cancellation.Token.ThrowIfCancellationRequested();
            if (!Live()) { throw new OperationCanceledException("Private content search changed."); }
            contentMatches = matches;
            contentPosition = position;
            contentSearchStatus = position.Complete
                ? "End of this bounded observation. Matches are exact retained USER messages and ASSISTANT answers, not new authority."
                : "Bounded observation only. Next continues across messages and conversations, including after an empty page.";
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (Live())
            {
                RetireContentSearch();
                contentSearchStatus = exception is ArgumentException
                    ? "Invalid query or continuation. Use 1-32 lexical terms, at most 256 characters / 512 UTF-8 bytes; start a fresh Search. No results or submission were published."
                    : exception is OperationCanceledException
                        ? "Content search cancelled. No late matches retained."
                        : "Content search unavailable or source changed. No partial results; start a fresh Search.";
            }
            Failure(logger, exception.GetType().Name);
            activity.Complete(exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        }
        finally
        {
            contentCancellation = null;
            contentSearching = false;
            NotifyContentSearch();
        }
    }

    public async Task OpenContentMatchAsync(ContentMatch match)
    {
        if (!CanSearchContent || contentControl != access.ControlRevision
            || !contentMatches.Any(row => ReferenceEquals(row, match)))
        {
            contentSearchStatus = "Select an actual current content-search match. No conversation or latest message was substituted.";
            NotifyContentSearch();
            return;
        }
        var epoch = contentEpoch;
        var control = access.ControlRevision;
        var selection = selectionEpoch;
        bool Live() => !closed && access.CanInspect && access.ControlRevision == control && contentEpoch == epoch
            && (selectionEpoch == selection || selectionEpoch == selection + 1 && selected == match.Target);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        contentCancellation = cancellation;
        contentOpening = true;
        NotifyContentSearch();
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), match.Target.Authority.SessionId,
            new(Guid.NewGuid()), RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
        try
        {
            await conversation!.ReadDetailAsync(match.Target, match.Message, Live, cancellation.Token, match.Assistant);
            if (!Live()) { throw new OperationCanceledException("The exact content match changed."); }
            await SelectExactAsync(match.Target.Authority.SessionId, Live, cancellation.Token);
            if (!Live() || selected != match.Target)
            { throw new OperationCanceledException("The original conversation source changed. Search again."); }
            await RunConversationAsync(async (target, selectedLive) =>
            {
                bool Admitted() => Live() && selectedLive();
                var cursor = new ConversationCursor(target.Authority.SessionId, target.Authority.Generation,
                    match.Ceiling, match.Message.Sequence - 1, match.HeadRevision);
                var page = await conversation.ReadPageAsync(target.Authority.SessionId, cursor, 1, cancellation.Token);
                if (!Admitted() || page.Session != target.Authority || page.Ceiling != match.Ceiling
                    || page.HeadRevision != match.HeadRevision || page.Messages.Length != 1 || page.Messages[0] != match.Message)
                { throw new ConversationSnapshotChangedException(); }
                await conversation.ReadDetailAsync(target, match.Message, Admitted, cancellation.Token, match.Assistant);
                if (!Admitted()) { throw new OperationCanceledException("Exact message presentation was retired."); }
                conversationPage = page;
                locatedContentPage = page;
                completedConversationMessage = null;
                conversationSourceHeld = false;
                composerAuthority = target.Authority;
                SelectedAnswerRecord = page.Messages[0];
                conversationStatus = "Located the exact retained search match in its owning conversation. Original message and answer are unchanged; selection does not execute, copy or speak.";
            }, sourceRead: true);
            contentSearchStatus = ConversationMessages.Any(message => ReferenceEquals(message, SelectedAnswerRecord))
                && SelectedAnswerRecord == match.Message
                ? "Exact message located. Continue reading here or use its existing deliberate message actions."
                : "The exact retained match is unavailable or changed. Search again; no latest-message fallback.";
            activity.Complete(SelectedAnswerRecord == match.Message ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (!closed)
            {
                RevokeLocatedContent(match.Target.Authority.SessionId);
                RetireContentSearch();
                contentSearchStatus = "The original match, conversation or private admission changed. Search again; no replacement message was opened.";
            }
            Failure(logger, exception.GetType().Name);
            activity.Complete(exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        }
        finally
        {
            contentCancellation = null;
            contentOpening = false;
            NotifyContentSearch();
        }
    }

    private void ObserveContentSources()
    {
        if (observingContentSources) { return; }
        observingContentSources = true;
        service.SessionMetadataChanging += OnContentSourceChanging;
        service.SessionLifecycleChanged += OnContentSourceChanging;
        service.SessionRetired += OnContentSourceChanging;
    }

    private void OnContentSourceChanging(HostId<SessionIdentity> session)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Invoke(() => OnContentSourceChanging(session));
            return;
        }
        RevokeLocatedContent(session);
        ClearContentSearch();
    }

    private void RevokeLocatedContent(HostId<SessionIdentity> session)
    {
        if (locatedContentPage is null || !ReferenceEquals(conversationPage, locatedContentPage)
            || selected?.Authority.SessionId != session) { return; }
        locatedContentPage = null;
        conversationPage = null;
        completedConversationMessage = null;
        SelectedAnswerRecord = null;
        conversationSourceHeld = true;
        revokeConversationDetails?.Invoke(session);
        conversationStatus = "The located message source changed. Its presentation was retired; refresh the exact conversation. Unsent input is unchanged.";
        NotifyConversation();
    }

    private void CloseContentSearch()
    {
        if (observingContentSources)
        {
            service.SessionMetadataChanging -= OnContentSourceChanging;
            service.SessionLifecycleChanged -= OnContentSourceChanging;
            service.SessionRetired -= OnContentSourceChanging;
            observingContentSources = false;
        }
        locatedContentPage = null;
        ClearContentSearch();
    }

    private void NotifyContentSearch()
    {
        if (!ReferenceEquals(conversationPage, locatedContentPage)) { locatedContentPage = null; }
        if (closed || !access.CanInspect || contentControl != access.ControlRevision)
        {
            RetireContentSearch();
            contentQuery = string.Empty;
        }
        foreach (var name in new[] { nameof(ContentQuery), nameof(ContentMatches), nameof(ContentSearchStatus),
            nameof(CanSearchContent), nameof(CanNextContentSearch), nameof(CanCancelContentSearch) })
        { OnPropertyChanged(name); }
    }

    public sealed record ContentMatch(SessionWorkspaceEntry Target, ConversationMessage Message, bool Assistant,
        long Ceiling, long HeadRevision)
    {
        public string Label => (Target.Metadata?.Name.Value ?? "Unnamed conversation") + (Assistant ? ": ASSISTANT answer" : ": USER message");
        public string Text => Assistant ? Message.AssistantResponse!.Body : Message.Body;
    }

    private sealed record ContentSearchPosition(SessionWorkspaceEntry? Target, SessionListSearchCursor? AfterSession,
        bool Assistant, ConversationSearchCursor? AfterMessage, bool Complete);
}
