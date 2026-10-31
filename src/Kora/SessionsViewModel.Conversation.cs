using System.Globalization;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private string composerBody = string.Empty;
    private ConversationPage? conversationPage;
    private ConversationMessage? completedConversationMessage;
    private bool conversationHeld;
    private bool conversationSourceHeld;
    private bool conversationBusy;
    private bool protectingComposer;
    private long composerBufferRevision;
    private string conversationStatus = "Select a session. Unsent messages stay in memory per session; nothing is autosaved.";
    internal Func<Task<ComposerDraftDecision>>? DecideComposerDraft { get; set; }
    internal void ReportComposerCloseFailure(Exception exception)
    {
        Failure(logger, exception.GetType().Name);
        conversationStatus = "Discard/close is unconfirmed; unsent messages remain in memory. Nothing was saved or submitted.";
        NotifyConversation();
    }
    public bool ComposerDirty => composerBody.Length != 0;
    public string ComposerBody
    {
        get => !closed && access.CanInspect && composerControl == access.ControlRevision ? composerBody : string.Empty;
        set
        {
            if (closed || !access.CanInspect || conversationBusy || protectingComposer) { return; }
            if (selected is null)
            {
                if (value.Length != 0)
                {
                    Interlocked.Increment(ref composerBufferRevision);
                    conversationStatus = "Unsent input refused: select an exact session first. Pending native input admission was retired; nothing saved or submitted.";
                    NotifyConversation();
                }
                return;
            }
            RestoreUnsent();
            CancelLocalRequestProfile();
            ClearExpiredOrdinary();
            CancelTaskOutputReview();
            RetireReplyReview();
            composerBody = value;
            Interlocked.Increment(ref composerBufferRevision);
            RememberUnsent();
            NotifyConversation();
        }
    }
    public string ConversationStatus => conversationStatus;
    public string ComposerTarget => selected is { } target
        ? "Conversation: " + (target.Metadata?.Name.Value ?? "Unnamed conversation")
            + "\nSession: " + target.Authority.SessionId.Value.ToString("D")
        : "Select an exact session to write a message.";
    public IReadOnlyList<ConversationMessage> ConversationMessages => conversationPage is { } page ? page.Messages
        : completedConversationMessage is { } completed ? [completed] : [];
    public bool CanReadConversation => CanRead && !conversationBusy && selected is not null && conversation is not null;
    public bool CanNextConversation => CanReadConversation && conversationPage?.Next is not null;
    public bool CanOpenConversationDetails => CanReadConversation && openConversationDetail is not null;
    internal bool CanPresentDetails => !closed && access.CanInspect;
    public bool CanShowDetails => CanRead && selected is not null
        && (CanOpenConversationDetails && ConversationMessages.Count != 0 || CanOpenHistoryDetail || HasEvidenceDetails);
    public string DetailsAvailability => CanShowDetails ? "Open an exact result from this session's observed messages or selected activity."
        : "No available details on the selected session's observed page. Select a session and explicitly read conversation or activity.";
    private Guid? dismissedSessionDetailsOffer;
    public ConversationMessage? DetailsOfferMessage => CanPresentDetails && completedConversationMessage is { AssistantResponse: { } response } message
        && message.Request.RequestId.Value != dismissedSessionDetailsOffer
        && Kora.Core.Presentation.AnswerPresentation.From(response.Body).HasDetails ? message : null;
    internal void DismissDetailsOffer(ConversationMessage message)
    {
        if (!ReferenceEquals(message, DetailsOfferMessage)) { return; }
        dismissedSessionDetailsOffer = message.Request.RequestId.Value;
        NotifyConversation();
    }
    public Task OpenConversationDetailAsync(ConversationMessage message, bool assistant = false, Guid? observationId = null,
        Func<bool>? detailAdmission = null) => RunConversationAsync(async (target, live) =>
    {
        if (!ConversationMessages.Concat(ConversationSearchMessages).Any(record => ReferenceEquals(record, message)))
        { throw new InvalidOperationException("Select an actual record from the currently observed exact conversation page."); }
        var searchMatch = ConversationSearchMessages.Any(record => ReferenceEquals(record, message));
        var searchEpoch = conversationSearchEpoch;
        bool DetailLive() => live() && (detailAdmission?.Invoke() ?? true) && (!searchMatch || conversationSearchEpoch == searchEpoch);
        if (searchMatch) { conversationSearchDetailTarget = target; }
        var content = await conversation!.ReadDetailAsync(target, message, DetailLive, lifetime.Token,
            assistant, observationId);
        if (!DetailLive()) { throw new OperationCanceledException("The original conversation selection or search changed."); }
        var beginValidation = HostActivity.CaptureContinuation(HostActivityLayer.Desktop, HostOperation.Storage);
        async Task<Kora.Core.Presentation.AdmittedDetailContent> Resolve(CancellationToken token)
        {
            using var validation = beginValidation();
            try
            {
                var exact = await conversation.ReadDetailAsync(target, message, DetailLive, token,
                    assistant, observationId);
                validation.Complete(HostOperationOutcome.Completed);
                return exact;
            }
            catch (OperationCanceledException)
            {
                validation.Complete(HostOperationOutcome.Cancelled);
                throw;
            }
            catch
            {
                validation.Complete(HostOperationOutcome.Failed);
                throw;
            }
        }
        var detailStatus = await (openConversationDetail
            ?? throw new InvalidOperationException("The native conversation detail viewer is unavailable."))(
                content, DetailLive, Resolve, lifetime.Token);
        if (!DetailLive()) { throw new OperationCanceledException("Search match navigation was revoked."); }
        conversationStatus = detailStatus;
        RememberSelectedDetail(observationId is null ? assistant ? DetailPart.Assistant : DetailPart.Original : DetailPart.Observation,
            message.Request.RequestId.Value, observationId);
    }, sourceRead: true, searchDetailEpoch:
        ConversationSearchMessages.Any(record => ReferenceEquals(record, message)) ? conversationSearchEpoch : null);
    public bool CanCompose => CanReadConversation && access.CanControl && selected?.Authority.IsActive == true
        && !conversationHeld && !conversationSourceHeld;
    public bool CanSubmitConversation => CanCompose && !string.IsNullOrWhiteSpace(composerBody)
        && (!assistantReply || ReplySourceCurrent);
    public async Task DiscardComposerBufferAsync()
    {
        if (!CanReadConversation) { return; }
        try
        {
            if (!await ConfirmUnsentDiscardAsync(all: false)) { return; }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReportComposerCloseFailure(exception);
            return;
        }
        composerBody = string.Empty;
        RememberUnsent();
        conversationStatus = "This session's unsent message explicitly discarded. Legacy saved text and submitted history are unchanged.";
        NotifyConversation();
    }

    public Task ReadConversationAsync(bool next = false) => RunConversationAsync(async (target, live) =>
    {
        var page = await conversation!.ReadPageAsync(target.Authority.SessionId,
            next ? conversationPage?.Next ?? throw new InvalidOperationException("No conversation continuation; refresh the exact source.") : null,
            25, lifetime.Token);
        if (!live() || page.Session != target.Authority)
        { throw new OperationCanceledException("Conversation source or exact selected generation changed."); }
        conversationPage = page;
        completedConversationMessage = null;
        conversationSourceHeld = false;
        composerAuthority = target.Authority;
        RememberUnsent();
        conversationStatus = "Observed " + page.Messages.Length.ToString(CultureInfo.InvariantCulture)
            + " exact immutable user messages with any separately retained ASSISTANT answer. HOST status is not assistant prose. "
            + (page.Next is null ? "End of this sequence snapshot." : "More bounded records available.")
            + (conversationHeld ? " Submission uncertainty remains held; inspect task receipts/restart recovery, never retry automatically." : string.Empty);
    }, sourceRead: true);

    public Task SubmitConversationAsync() => assistantReply ? ReviewAssistantReplyAsync() : SubmitOrdinaryConversationAsync(localAnswer: false);
    public Task SubmitLocalAnswerAsync() => assistantReply
        ? SubmitConversationCoreAsync(allowQuestion: false, localAnswer: true)
        : SubmitOrdinaryConversationAsync(localAnswer: true);
    public Task SubmitCopilotConversationAsync() => SubmitConversationCoreAsync(allowQuestion: false, copilot: true);
    public bool CanSubmitLocalFirst => CanSubmitConversation && localHandoff is not null && !assistantReply;
    public Task SubmitLocalFirstAsync() => SubmitConversationCoreAsync(allowQuestion: false, localFirst: true);

    private Task SubmitConversationCoreAsync(bool allowQuestion, Uri? pageAddress = null, bool copilot = false,
        bool useMcp = false, string? mcpBody = null, RequestOrigin mcpOrigin = RequestOrigin.LocalUi,
        Func<bool>? mcpCallerAdmission = null, bool claude = false, bool claudeObservations = false,
        ModelToolMode clarificationTools = ModelToolMode.None, bool localAnswer = false, bool claudeStreamAnswer = false,
        bool localFirst = false, CancellationToken mcpToken = default) => RunConversationAsync(async (target, live) =>
    {
        if (conversationHeld) { throw new InvalidOperationException("An unconfirmed submission is held; inspect without retry."); }
        var body = mcpBody ?? composerBody;
        var presenter = allowQuestion ? QuestionPresenter
            ?? throw new InvalidOperationException("The exact native clarification presenter is unavailable. Nothing was submitted.") : null;
        if (useMcp && !HasCurrentMcpOptions)
        { throw new InvalidOperationException("Refresh explicit MCP options for this exact session before submission."); }
        var mcpRevision = Interlocked.Read(ref mcpSelectionRevision);
        var observationIds = ClaudeObservationIds;
        var observationRevision = Interlocked.Read(ref claudeObservationRevision);
        var receipt = localFirst
            ? await (localHandoff ?? throw new InvalidOperationException("The verified local-first operation is not composed."))
                .RunAsync(target, body, live, lifetime.Token)
            : useMcp
            ? await conversation!.SubmitMcpAsync(target, body,
                mcpServer is { } server && mcpServers.Any(item => ReferenceEquals(item, server)) ? server.Id
                    : throw new InvalidOperationException("Select an actual app-included MCP server from refreshed options."),
                mcpAccount is { } account && mcpAccounts.Any(item => ReferenceEquals(item, account)) ? account.Reference
                    : throw new InvalidOperationException("Select an actual delegated MCP account from refreshed options."),
                McpPresenter ?? throw new InvalidOperationException("The exact native MCP review presenter is unavailable."),
                McpSelector ?? throw new InvalidOperationException("The exact native MCP tool selector is unavailable."),
                () => live() && Interlocked.Read(ref mcpSelectionRevision) == mcpRevision
                    && mcpCallerAdmission?.Invoke() != false, mcpToken, mcpOrigin, (request, result) =>
                {
                    if (!live() || Interlocked.Read(ref mcpSelectionRevision) != mcpRevision) { return; }
                    mcpResult = result;
                    mcpResultRequest = request;
                    mcpResultControl = access.ControlRevision;
                })
            : localAnswer
            ? await conversation!.SubmitLocalAnswerAsync(target, body, live, lifetime.Token)
            : claude
            ? claudeObservations
                ? await conversation!.SubmitClaudeObservationsAsync(target, body, observationIds.Split(','),
                    () => live() && Interlocked.Read(ref claudeObservationRevision) == observationRevision, lifetime.Token)
                : claudeStreamAnswer
                    ? await conversation!.SubmitClaudeAnswerStreamAsync(target, body, live, lifetime.Token)
                    : allowQuestion
                    ? await conversation!.SubmitClaudeWithClarificationAsync(target, body, live, presenter!, lifetime.Token)
                    : await conversation!.SubmitClaudeAsync(target, body, live, lifetime.Token)
            : copilot
            ? await conversation!.SubmitCopilotAsync(target, body, live, lifetime.Token)
            : clarificationTools == ModelToolMode.ReadOnly
            ? await conversation!.SubmitReadOnlyWithClarificationAsync(target, body, live, presenter!, lifetime.Token)
            : pageAddress is null
            ? await conversation!.SubmitAsync(target, body, live, lifetime.Token, questionPresenter: presenter)
            : await conversation!.SubmitWebPageAsync(target, body, pageAddress, WebPagePresenter
                ?? throw new InvalidOperationException("The exact native page approval presenter is unavailable. Nothing was submitted."),
                live, lifetime.Token);
        if (!live()) { throw new OperationCanceledException("The selected private presentation changed."); }
        if (string.Equals(composerBody, body, StringComparison.Ordinal)) { composerBody = string.Empty; }
        RememberUnsent();
        conversationPage = null;
        completedConversationMessage = receipt.Message;
        conversationStatus = "Original user message retained exactly; request " + receipt.Message.Request.RequestId.Value.ToString("D")
            + " | sequence " + receipt.Message.Sequence.ToString(CultureInfo.InvariantCulture)
            + " | HOST " + receipt.Message.Outcome + " / " + receipt.Message.Reason
            + " | task " + receipt.Task.State
            + (receipt.Message.AssistantResponse is null ? ". No assistant answer retained." : ". Genuine ASSISTANT answer retained separately below.")
            + " No fallback or automatic retry. Refresh for the full bounded conversation.";
    }, submission: true, origin: mcpOrigin);

    internal Task<bool> ProtectComposerAsync(Func<bool>? admitted = null)
    {
        if (admitted?.Invoke() == false) { throw new OperationCanceledException("Original composer navigation admission changed."); }
        RememberUnsent();
        return Task.FromResult(!protectingComposer);
    }

    private async Task RunConversationAsync(Func<Kora.Core.Storage.SessionWorkspaceEntry, Func<bool>, Task> operation,
        bool submission = false, bool sourceRead = false, long? searchDetailEpoch = null, bool legacyRecovery = false,
        RequestOrigin origin = RequestOrigin.LocalUi, bool replyOperation = false)
    {
        if (!CanReadConversation) { return; }
        if (replyOperation)
        {
            try { Kora.Application.Hosting.SessionConversationService.RequireOriginalSource(origin); }
            catch (InvalidOperationException exception)
            {
                Failure(logger, exception.GetType().Name);
                conversationStatus = "Reply requires live original native user authority without channel relabelling. Nothing submitted.";
                NotifyConversation();
                return;
            }
        }
        var target = RequireSelected();
        var epoch = selectionEpoch;
        var revision = access.ControlRevision;
        bool Live() => !closed && access.CanInspect && access.ControlRevision == revision
            && selectionEpoch == epoch && selected == target;
        conversationBusy = true;
        busy = true;
        Notify();
        using var activity = HostActivity.BeginRoot(new(new(Guid.NewGuid()), target.Authority.SessionId,
            new(Guid.NewGuid()), origin), HostActivityLayer.Desktop, HostOperation.Request);
        try
        {
            if (submission && assistantReply && !replyOperation)
            { throw new InvalidOperationException("Reply mode requires its own exact source review and separate local-only confirmation. Cancel reply before another submission route."); }
            if (submission && (conversationHeld || conversationSourceHeld))
            { throw new InvalidOperationException("Submission is held; inspect the exact conversation/task receipts before proceeding."); }
            await operation(target, Live);
            if (!Live()) { throw new OperationCanceledException("Private conversation presentation was revoked."); }
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            var searchNavigationRevoked = searchDetailEpoch is { } searchEpoch && searchEpoch != conversationSearchEpoch && Live();
            if (searchNavigationRevoked)
            {
                conversationStatus = "Search match navigation revoked. Composer and saved draft unchanged; explicit fresh Search required.";
            }
            else if (legacyRecovery && Live())
            {
                conversationStatus = "Legacy recovery unavailable: " + exception.Message
                    + " Unsent input and conversation history unchanged. Nothing saved, removed or submitted; a fresh explicit recovery reads the source again.";
            }
            else if (sourceRead && exception is ConversationSnapshotChangedException && Live())
            {
                ClearConversationSearch();
                conversationPage = null;
                completedConversationMessage = null;
                conversationStatus = "Terminal snapshot changed; Refresh conversation. Memory draft retained; no retry or submission.";
            }
            else if (sourceRead)
            {
                ClearConversationSearch();
                conversationPage = null;
                completedConversationMessage = null;
                conversationSourceHeld = true;
                conversationStatus = "Exact conversation source unavailable or changed. Unsent input retained and held; refresh the exact session before proceeding.";
            }
            else if (Live())
            {
                conversationHeld |= submission && exception is ConversationSubmissionUnconfirmedException;
                conversationStatus = "Unconfirmed: " + exception.Message
                    + " Memory draft retained; no retry, rollback or successful submission inferred. Read exact conversation/task receipts.";
                if (exception is ConversationSubmissionUnconfirmedException uncertain)
                {
                    conversationStatus += " Request " + uncertain.Request.RequestId.Value.ToString("D")
                        + " | task " + uncertain.Request.TaskId.Value.ToString("D") + ".";
                }
            }
            else if (closed || !access.CanInspect || access.ControlRevision != revision) { ClearConversation(); }
            else
            {
                conversationPage = null;
                completedConversationMessage = null;
                conversationHeld |= submission;
                conversationStatus = "The original selection changed during admission; its memory draft remains held, never retargeted. Inspect exact receipts.";
            }
            Failure(logger, exception.GetType().Name);
            activity.Complete(searchNavigationRevoked || exception is OperationCanceledException
                ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        }
        finally
        {
            RememberUnsent();
            conversationBusy = false;
            busy = false;
            Notify();
            if (closed) { lifetime.Dispose(); }
        }
    }

    private void ClearConversation(bool preserveUnsent = true)
    {
        CancelTaskOutputReview();
        CancelLocalRequestProfile();
        ClearFailedRetry();
        ClearExpiredOrdinary();
        ClearMixedContext();
        ClearAnswerRead();
        ClearInstructionContext();
        ClearHistoryContext();
        ClearConversationCancellation();
        CancelAppearanceSuggestion(reportCancellation: false);
        CancelSpeechSummarySuggestion(reportCancellation: false);
        CancelPlaybackVolumeSuggestion(reportCancellation: false);
        CancelModelMemoryProposal();
        if (preserveUnsent) { RememberUnsent(); }
        ClearAssistantReply();
        if (selected is { } target) { revokeConversationDetails?.Invoke(target.Authority.SessionId); }
        ClearConversationSearch();
        composerBody = string.Empty;
        conversationPage = null;
        completedConversationMessage = null;
        conversationHeld = false;
        conversationSourceHeld = false;
        composerSession = null;
        conversationStatus = "Private conversation cleared. Explicitly read an exact session before restoring saved data.";
    }

    private void NotifyConversation()
    {
        NotifyTaskOutput();
        NotifyLocalRequestProfile();
        NotifyFailedRetry();
        NotifyExpiredOrdinary();
        RestoreUnsent();
        NotifyAssistantReply();
        NotifyMixedContext();
        NotifyModelMemoryProposal();
        NotifyAnswerRead();
        ObserveConversationCancellation();
        NotifyConversationCancellation();
        if (appearanceReview is { Available: false }) { CancelAppearanceSuggestion(reportCancellation: false); }
        OnPropertyChanged(nameof(AppearanceSuggestionDescription));
        OnPropertyChanged(nameof(CanConfirmAppearanceSuggestion));
        OnPropertyChanged(nameof(CanReviewAppearanceSuggestion));
        if (speechSummaryReview is { Available: false }) { CancelSpeechSummarySuggestion(reportCancellation: false); }
        OnPropertyChanged(nameof(SpeechSummarySuggestionDescription));
        OnPropertyChanged(nameof(CanConfirmSpeechSummarySuggestion));
        OnPropertyChanged(nameof(CanReviewSpeechSummarySuggestion));
        if (playbackVolumeReview is { Available: false }) { CancelPlaybackVolumeSuggestion(reportCancellation: false); }
        OnPropertyChanged(nameof(PlaybackVolumeSuggestionDescription));
        OnPropertyChanged(nameof(CanConfirmPlaybackVolumeSuggestion));
        OnPropertyChanged(nameof(CanReviewPlaybackVolumeSuggestion));
        NotifyRegisteredSettings();
        NotifyInstructionContext();
        NotifyHistoryContext();
        NotifyConversationSearch();
        foreach (var name in new[] { nameof(ComposerBody), nameof(ComposerDirty), nameof(ComposerTarget),
            nameof(ConversationMessages), nameof(ConversationStatus), nameof(CanReadConversation), nameof(CanNextConversation),
            nameof(CanOpenConversationDetails), nameof(CanReviewOriginalMessageReuse), nameof(CanCompose), nameof(CanSubmitConversation), nameof(CanSubmitLocalFirst),
            nameof(CanShowDetails), nameof(DetailsAvailability),
            nameof(HasEvidenceDetails), nameof(EvidenceDetails), nameof(ObservationChoices),
            nameof(DetailsOfferMessage),
            nameof(CanRecoverConversationDraft), nameof(ComposerDraftSizeFeedback), nameof(CanSubmitConversationWithClarification),
            nameof(McpServers), nameof(McpAccounts), nameof(SelectedMcpServer), nameof(SelectedMcpAccount), nameof(IsMcpActive), nameof(McpResultText) })
        { OnPropertyChanged(name); }
    }
}
