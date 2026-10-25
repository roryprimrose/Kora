using Avalonia.Threading;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Application.Interaction;
using Kora.Application.ViewModels;
using Kora.Core.Storage;
using Kora.Core.Commands;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class SessionsWindowController(
    MainViewModel main, SessionWorkspaceService service, DurableEvidenceQuery evidence,
    ISessionWorkspaceAccess access, ILogger<SessionsViewModel> logger, LocalEventBroker? localEvents = null,
    DetailWindowController? details = null, Kora.Application.Memory.MemoryManagementService? memories = null,
    SessionAttachmentWindowController? attachments = null,
    Kora.Application.Configuration.SessionProviderProfileConfigurationService? providers = null,
    SessionConversationService? conversation = null, ScopedInstructionUseBroker? instructionUse = null) : IDisposable, ISessionDispositionReviewPresenter,
    ISessionQueueCancellationReviewPresenter
{
    private SessionsWindow? window;
    private CompactSessionWindow? compact;
    private ResponseWindow? interaction;
    private bool interactionListInitialized;
    private SessionsViewModel? model;
    private SessionsPresentationOwner? presentation;
    private bool disposed;
    private long dispositionOwnerEpoch;
    private int dispositionOwnerOpen;
    private int compactAnswerReadOpen;
    internal Func<SessionsViewModel, SessionsPresentationOwner, ResponseWindow>? CreateInteraction { get; set; }
    internal Task InteractionOperation { get; private set; } = Task.CompletedTask;

    internal async Task ShowInteractionAsync(bool expanded, Func<bool> admitted)
    {
        if (disposed || !access.CanInspect || !main.CanRevealPrivatePresentation || !admitted())
        {
            main.ReportHostInteractionFailure("Session navigation unavailable: original private desktop admission changed.");
            return;
        }
        if (CreateInteraction is null)
        {
            if (expanded) { Open(); }
            else { OpenCompact(admitted); }
            return;
        }
        EnsurePresentation();
        var state = model!;
        var owner = presentation!;
        var control = access.ControlRevision;
        if (interaction is null)
        {
            var opened = CreateInteraction(state, owner);
            interaction = opened;
            opened.Closed += (_, _) =>
            {
                if (ReferenceEquals(interaction, opened)) { interaction = null; }
                ReleaseUnusedPresentation();
            };
        }
        var surface = interaction;
        bool Live() => !disposed && admitted() && access.CanInspect && access.ControlRevision == control
            && main.CanRevealPrivatePresentation && ReferenceEquals(model, state)
            && ReferenceEquals(presentation, owner) && ReferenceEquals(interaction, surface);
        surface.ShowSessions(expanded);
        if (!Live())
        {
            owner.Revoke();
            main.ReportHostInteractionFailure("Session navigation retired before publication.");
            return;
        }
        if (expanded && !state.HasSessionListPage && state.CanRead)
        {
            if (!interactionListInitialized) { state.SessionFilter = SessionListFilter.Active; }
            interactionListInitialized = true;
            await state.RefreshAsync();
            if (!Live()) { owner.Revoke(); }
        }
    }

    internal Func<Task> CaptureSessionSelection(int? position, Func<bool> admitted)
    {
        var source = model;
        var surface = interaction;
        var rows = source?.Sessions.ToArray() ?? [];
        var index = position == WorkspaceSurfaceCommand.LastSessionPosition ? rows.Length - 1 : position.GetValueOrDefault() - 1;
        if (source is null || !source.CanRead || surface?.IsSessionListPresented != true || position is null
            || index < 0 || index >= rows.Length)
        {
            return () =>
            {
                main.ReportHostInteractionFailure("Session selection unavailable. Show sessions first and choose a position on its loaded page; no replacement was selected.");
                return Task.CompletedTask;
            };
        }
        var row = rows[index];
        var rowLive = source.CapturePresentedRow(row);
        var surfaceLive = surface.CaptureSessionListPresentation();
        bool Live() => !disposed && admitted() && ReferenceEquals(model, source)
            && ReferenceEquals(interaction, surface) && surfaceLive() && rowLive();
        return async () =>
        {
            if (!Live() || !source.CanRead)
            {
                main.ReportHostInteractionFailure("Session selection cancelled: the originally presented list changed. Show sessions and choose again.");
                return;
            }
            await source.SelectAsync(row, Live);
            if (Live() && source.SelectedSessionRecord == row)
            { source.ReportNavigationStatus("Selected " + source.InteractionTarget + ". No question, permission or work was changed."); }
        };
    }

    internal Func<bool> CaptureCompactNavigationEligibility()
    {
        var originalModel = model;
        var originalPresentation = presentation;
        var source = model?.CaptureNavigationEligibility() ?? (static () => true);
        return () => !disposed && ReferenceEquals(model, originalModel)
            && ReferenceEquals(presentation, originalPresentation) && source();
    }
    internal Action? OpenAllWork { get; set; }
    internal Action? OpenNewRequest { get; set; }
    internal QuestionWindowController? ConversationQuestions { get; set; }
    internal Task<bool> PrepareShutdown() => presentation?.PrepareShutdown() ?? Task.FromResult(true);
    internal Func<bool> CaptureShutdownEligibility()
    {
        var state = model;
        var owner = presentation;
        var workspaceView = window;
        var compactView = compact;
        var interactionView = interaction;
        var revision = access.ControlRevision;
        var admitted = !disposed && access.CanInspect && access.CanControl;
        var buffer = state?.CaptureUnsentShutdownEligibility() ?? (static () => true);
        return () => admitted && !disposed && access.ControlRevision == revision
            && ReferenceEquals(model, state) && ReferenceEquals(presentation, owner)
            && ReferenceEquals(window, workspaceView) && ReferenceEquals(compact, compactView)
            && ReferenceEquals(interaction, interactionView) && buffer();
    }
    internal Func<SessionWorkspaceEntry, Kora.Core.Conversation.ConversationMessage, Avalonia.Controls.Window?,
        Func<bool>, CancellationToken, Task<string>>? ReviewOriginalMessageReuse { get; set; }

    internal void Bind()
    {
        main.SessionsRequested += OnOpen;
        main.BindSelectedAnswerRead(eligible => !disposed
            && (Volatile.Read(ref dispositionOwnerOpen) != 0 || Volatile.Read(ref compactAnswerReadOpen) != 0)
            ? model?.CaptureSelectedAnswerRead(() => eligible() && !disposed
                && (Volatile.Read(ref dispositionOwnerOpen) != 0 || Volatile.Read(ref compactAnswerReadOpen) != 0)) : null);
        main.BindSessionDispositionReview(this);
        main.BindMcpCommands(async (command, origin, token) =>
        {
            var active = model;
            var target = active?.SelectedSessionRecord;
            var revision = access.ControlRevision;
            bool Live() => !disposed && access.CanControl && access.ControlRevision == revision
                && ReferenceEquals(model, active) && target is not null && active?.SelectedSessionRecord == target
                && (window?.IsActive == true || compact?.IsActive == true || interaction?.IsActive == true
                    || active?.IsMcpActive == true && ConversationQuestions?.IsOwnerInUse == true);
            if (!Live()) { throw new InvalidOperationException("MCP commands require the exact foreground selected session and explicit native server/account choices."); }
            token.ThrowIfCancellationRequested();
            if (command.Stop) { await active!.CancelMcpConversationAsync(); return "Cancellation requested; owned work remains held until actual termination. No rollback or automatic retry."; }
            var message = await active!.SubmitMcpConversationAsync(command.Body, origin, Live);
            if (!Live()) { throw new OperationCanceledException("The original MCP presentation changed; no result is disclosed to another target."); }
            return message?.AssistantResponse?.Body ?? active.ConversationStatus;
        });
        main.BindQueueCancellationReview(this);
        main.PrivacyClosureRequested += OnPrivacyClosure;
        service.SessionLifecycleChanged += RevokeSession;
        service.SessionRetired += RevokeSession;
        service.SessionMetadataChanging += OnAnswerSourceChanging;
    }

    internal void Open()
    {
        if (disposed || !access.CanInspect)
        {
            main.ReportHostInteractionFailure("Sessions unavailable: live private desktop ownership is required.");
            return;
        }

        if (window is null)
        {
            EnsurePresentation();
            var opened = new SessionsWindow(model!, presentation);
            opened.AllWorkRequested += (_, _) => OpenAllWork?.Invoke();
            opened.NewRequestRequested += (_, _) => OpenNewRequest?.Invoke();
            opened.CompactConversationRequested += (_, _) => OpenCompact();
            opened.PropertyChanged += (_, args) =>
            {
                if (args.Property != Avalonia.Controls.Window.IsVisibleProperty || !ReferenceEquals(window, opened)) { return; }
                Volatile.Write(ref dispositionOwnerOpen, opened.IsVisible ? 1 : 0);
                if (!opened.IsVisible)
                {
                    Interlocked.Increment(ref dispositionOwnerEpoch);
                    model?.RevokeDispositionReview();
                    model?.CancelAnswerRead();
                }
            };
            opened.Closed += (_, _) =>
            {
                if (ReferenceEquals(window, opened))
                {
                    Volatile.Write(ref dispositionOwnerOpen, 0);
                    Interlocked.Increment(ref dispositionOwnerEpoch);
                    model?.RevokeDispositionReview();
                    window = null;
                }
                ReleaseUnusedPresentation();
            };
            window = opened;
            opened.Show();
        }
        window.Activate();
    }

    internal void OpenCompact() => OpenCompact(static () => true);
    internal void OpenCompact(Func<bool> admitted)
    {
        if (CreateInteraction is not null)
        {
            InteractionOperation = ShowInteractionAsync(false, admitted);
            return;
        }
        if (disposed || !access.CanInspect || !admitted())
        {
            main.ReportHostInteractionFailure("Compact conversation unavailable: live private desktop ownership is required.");
            return;
        }
        var source = model?.CaptureNavigationEligibility() ?? (static () => true);
        if (compact is null)
        {
            EnsurePresentation();
            source = model!.CaptureNavigationEligibility();
            var opened = new CompactSessionWindow(model!, presentation!);
            opened.SessionsRequested += (_, _) => Open();
            opened.NewRequestRequested += (_, _) => OpenNewRequest?.Invoke();
            opened.PropertyChanged += (_, args) =>
            {
                if (args.Property != Avalonia.Controls.Window.IsVisibleProperty || !ReferenceEquals(compact, opened)) { return; }
                Volatile.Write(ref compactAnswerReadOpen, opened.IsVisible ? 1 : 0);
                if (!opened.IsVisible) { model?.CancelAnswerRead(); }
            };
            opened.Closed += (_, _) =>
            {
                if (ReferenceEquals(compact, opened)) { Volatile.Write(ref compactAnswerReadOpen, 0); compact = null; }
                ReleaseUnusedPresentation();
            };
            compact = opened;
            opened.Show();
        }
        if (disposed || !access.CanInspect || !admitted() || !source())
        {
            if (!access.CanInspect || !main.CanRevealPrivatePresentation) { presentation?.Revoke(); }
            main.ReportHostInteractionFailure("Compact conversation unavailable: opening admission changed. No selection or draft was loaded.");
            return;
        }
        compact?.Activate();
    }

    private void EnsurePresentation()
    {
        if (presentation is not null) { return; }
        interactionListInitialized = false;
        model = new(service, evidence, access, logger, localEvents,
            content => details?.OpenHistoryDetail(content, window ?? (Avalonia.Controls.Window?)compact ?? interaction)
                ?? "History details unavailable: the native viewer is not composed.",
            session => details?.RevokeSession(session), memories, attachments, providers, conversation,
            (content, admitted, resolve, token) => details?.OpenConversationDetailAsync(content,
                window ?? (Avalonia.Controls.Window?)compact ?? interaction, admitted, resolve, token)
                ?? Task.FromResult("Conversation details unavailable: the native viewer is not composed."),
            session => details?.RevokeConversationSession(session),
            (source, original, admitted, token) => ReviewOriginalMessageReuse?.Invoke(source, original,
                window ?? (Avalonia.Controls.Window?)compact ?? interaction, admitted, token)
                ?? Task.FromResult("Original USER reuse unavailable: native New request is not composed."),
            reviewAppearanceSuggestion: main.ReviewAppearanceSuggestionAsync,
            confirmAppearanceSuggestion: main.ConfirmAppearanceSuggestionAsync,
            instructionUse: instructionUse,
            reviewSpeechSummarySuggestion: main.ReviewSpeechSummarySuggestionAsync,
            confirmSpeechSummarySuggestion: (review, token) => main.ConfirmSpeechSummarySuggestionAsync(review, token),
            answerReads: new SessionAnswerReadWorkflow(service, main));
        if (ConversationQuestions is { } questions)
        {
            model.QuestionPresenter = (request, generation, spec, expiry, admitted, token) =>
                questions.AskConversationAsync(window ?? (Avalonia.Controls.Window?)compact ?? interaction
                    ?? throw new InvalidOperationException("The exact conversation window is unavailable."),
                    request, generation, spec, expiry, admitted, token);
            model.WebPagePresenter = async (proposal, generation, address, admitted, token) =>
            {
                var placeholder = new Kora.Core.Interaction.QuestionSpec("Exact page approval",
                    Kora.Core.Interaction.QuestionKind.SingleChoice, [new("once", "Approve once")]);
                var receipt = await questions.AskConversationAsync(window ?? (Avalonia.Controls.Window?)compact ?? interaction
                    ?? throw new InvalidOperationException("The exact conversation window is unavailable."),
                    proposal.Request, generation, placeholder, proposal.ExpiresAt, admitted, token, proposal, address);
                return new(receipt.Presented, receipt.Completed);
            };
            model.McpPresenter = async (proposal, generation, review, admitted, token) =>
            {
                var spec = new Kora.Core.Interaction.QuestionSpec("Exact HTTPS MCP approval",
                    Kora.Core.Interaction.QuestionKind.SingleChoice, [new("once", "Approve once")]);
                var receipt = await questions.AskConversationAsync(window ?? (Avalonia.Controls.Window?)compact ?? interaction
                    ?? throw new InvalidOperationException("The exact conversation window is unavailable."),
                    proposal.Request, generation, spec, proposal.ExpiresAt, admitted, token,
                    webProposal: proposal, mcpReview: review);
                return new(receipt.Presented, receipt.Completed);
            };
            model.McpSelector = async (request, generation, expiresAt, tools, admitted, token) =>
            {
                var options = tools.Select((tool, index) => new Kora.Core.Interaction.QuestionOption(
                    "tool-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture), tool.Name)).ToArray();
                var spec = new Kora.Core.Interaction.QuestionSpec("Select one discovered host-mapped read-only MCP tool. "
                    + "Its complete schema and outgoing arguments still require separate exact review.",
                    Kora.Core.Interaction.QuestionKind.SingleChoice, options, sourceId: "mcp-tool-selection");
                var receipt = await questions.AskConversationAsync(window ?? (Avalonia.Controls.Window?)compact ?? interaction
                    ?? throw new InvalidOperationException("The exact conversation window is unavailable."),
                    request, generation, spec, expiresAt, admitted, token, mcpSelection: true);
                if (receipt.Completed.Outcome != Kora.Core.Interaction.HostInteractionOutcome.Answered
                    || receipt.Completed.Question?.Draft is not { Choices.Length: 1 } answer)
                { return null; }
                var selectedOption = Array.FindIndex(options, option => string.Equals(option.Id, answer.Choices[0], StringComparison.Ordinal));
                return selectedOption < 0 ? null : tools[selectedOption].Name;
            };
        }
        presentation = new(model);
    }

    private void ReleaseUnusedPresentation()
    {
        if (window is not null || compact is not null || interaction is not null) { return; }
        model = null;
        presentation = null;
    }

    private void OnAnswerSourceChanging(HostId<SessionIdentity> session)
    {
        if (model?.ReferencesSession(session) == true) { model.CancelAnswerRead(); }
    }

    internal async Task NavigateAsync(Kora.Core.Hosting.HostId<Kora.Core.Hosting.SessionIdentity> session,
        Func<bool> admitted, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (disposed || !access.CanInspect || !admitted())
        {
            throw new OperationCanceledException("Exact overview navigation is no longer admitted.");
        }
        if (CreateInteraction is not null) { await ShowInteractionAsync(true, admitted); }
        else { Open(); }
        if (model is not { } target)
        {
            throw new InvalidOperationException("The Sessions window is unavailable.");
        }
        if (!target.CanRead) { throw new InvalidOperationException("Sessions is busy; retry passive navigation when idle."); }
        await target.SelectExactAsync(session, admitted, token);
    }

    internal async Task OpenRetentionReviewAsync(SessionRetentionReview review, Func<bool> admitted, CancellationToken token)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            await Dispatcher.UIThread.InvokeAsync(() => OpenRetentionReviewAsync(review, admitted, token));
            return;
        }
        token.ThrowIfCancellationRequested();
        if (disposed || !access.CanInspect || !access.CanControl || !main.CanRevealPrivatePresentation || !admitted())
        { throw new OperationCanceledException("Exact retention navigation is no longer admitted."); }
        Open();
        if (window?.DataContext is not SessionsViewModel target)
        { throw new InvalidOperationException("The native Sessions window is unavailable."); }
        await target.OpenRetentionReviewAsync(review, admitted, token);
    }

    public async Task<bool> ReviewDispositionAsync(SessionDispositionReviewCommand command, RequestOrigin origin,
        Func<bool> admitted, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (disposed || origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || command.Error is not null || !access.CanControl || !main.CanRevealPrivatePresentation || !admitted())
        { throw new OperationCanceledException("Private native disposition review is unavailable."); }
        Open();
        var owner = window ?? throw new InvalidOperationException("The native Sessions window is unavailable.");
        var state = model ?? throw new InvalidOperationException("The native Sessions presentation is unavailable.");
        var ownerEpoch = Volatile.Read(ref dispositionOwnerEpoch);
        // Storage callbacks consume native-event observations, never Avalonia properties off-thread.
        bool NativeAdmission() => !disposed && ReferenceEquals(window, owner)
            && Volatile.Read(ref dispositionOwnerOpen) == 1 && ownerEpoch == Volatile.Read(ref dispositionOwnerEpoch)
            && ReferenceEquals(model, state) && access.CanInspect && access.CanControl && main.CanRevealPrivatePresentation;
        return await state.ReviewDispositionAsync(command, origin, admitted, NativeAdmission, token);
    }

    internal void RevokeSession(Kora.Core.Hosting.HostId<Kora.Core.Hosting.SessionIdentity> session)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            // Lifecycle notifications run after storage I/O; revoke native content before the caller observes completion.
            Dispatcher.UIThread.Invoke(() => RevokeSession(session));
            return;
        }

        if (disposed) { return; }
        details?.RevokeSession(session);
        if (model is { } state)
        {
            state.RetireUnsent(session);
            if (state.ReferencesSession(session)) { presentation?.Revoke(); }
            else { state.RevokeSessionList(); }
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "Native navigation event boundary; failures are explicitly reported through the host.")]
    private async void OnOpen(object? sender, EventArgs args)
    {
        try { await (InteractionOperation = ShowInteractionAsync(true, static () => true)); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { main.ReportHostInteractionFailure("Sessions could not be opened. " + exception.Message); }
    }
    private void OnPrivacyClosure(object? sender, EventArgs args) => presentation?.Revoke();

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        Volatile.Write(ref dispositionOwnerOpen, 0);
        Interlocked.Increment(ref dispositionOwnerEpoch);
        OpenAllWork = null;
        OpenNewRequest = null;
        CreateInteraction = null;
        ReviewOriginalMessageReuse = null;
        main.BindSessionDispositionReview(null);
        main.BindMcpCommands(null);
        main.BindQueueCancellationReview(null);
        main.SessionsRequested -= OnOpen;
        main.PrivacyClosureRequested -= OnPrivacyClosure;
        main.BindSelectedAnswerRead(static _ => null);
        service.SessionLifecycleChanged -= RevokeSession;
        service.SessionRetired -= RevokeSession;
        service.SessionMetadataChanging -= OnAnswerSourceChanging;
        presentation?.Revoke();
        window = null;
        compact = null;
        interaction = null;
        model = null;
        presentation = null;
    }
}
