using Avalonia.Controls;
using Avalonia.Threading;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Application.Interaction;
using Kora.Application.ViewModels;
using Kora.Core.Storage;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Diagnostics;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class SessionsWindowController(
    MainViewModel main, SessionWorkspaceService service, DurableEvidenceQuery evidence,
    ISessionWorkspaceAccess access, ILogger<SessionsViewModel> logger, LocalEventBroker? localEvents = null,
    DetailWindowController? details = null, Kora.Application.Memory.MemoryManagementService? memories = null,
    SessionAttachmentWindowController? attachments = null,
    Kora.Application.Configuration.SessionProviderProfileConfigurationService? providers = null,
    SessionConversationService? conversation = null, ScopedInstructionUseBroker? instructionUse = null,
    SessionInstructionScopeCommandService? scopeCommands = null, SessionInstructionScopeBroker? scopeBroker = null,
    Kora.Application.Dependencies.VerifiedLocalHandoffOperation? localHandoff = null,
    SessionRetentionApplyService? retentionApply = null) : IDisposable, ISessionDispositionReviewPresenter,
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
    private SessionInstructionScopeController? instructionScopes;
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

    internal async Task ShowDetailsAsync(WorkspaceSurfaceTarget target, Func<bool> admitted)
    {
        var state = model;
        var selected = state?.SelectedSessionRecord;
        var live = state?.CaptureNavigationEligibility();
        bool Current() => !disposed && admitted() && state is not null && ReferenceEquals(model, state)
            && live!() && state.SelectedSessionRecord == selected;
        if (!Current() || selected is null)
        {
            main.ReportHostInteractionFailure("Details unavailable: select a session and explicitly read its conversation or activity first.");
            return;
        }
        if (target is WorkspaceSurfaceTarget.SessionLogs or WorkspaceSurfaceTarget.SessionAudit
            && HostActivity.Current?.Request.Origin != RequestOrigin.LocalUi)
        {
            main.ReportHostInteractionFailure("Log/audit inspection requires an original local UI request. Voice cannot be relabelled as native evidence admission.");
            return;
        }
        await ShowInteractionAsync(true, Current);
        if (!Current() || interaction is null) { return; }
        if (target == WorkspaceSurfaceTarget.HideDetails) { interaction.SessionView.HideDetailsAndRestoreFocus(); }
        else if (target is WorkspaceSurfaceTarget.SessionLogs or WorkspaceSurfaceTarget.SessionAudit)
        {
            await state!.ReadEvidenceDetailsAsync(target == WorkspaceSurfaceTarget.SessionLogs
                ? Kora.Core.Diagnostics.EvidenceSource.Log : Kora.Core.Diagnostics.EvidenceSource.AuthorityAudit);
        }
        else if (state!.CanShowDetails) { await interaction.SessionView.ShowAvailableDetailsAsync(state, acceptOffer: true); }
        else { main.ReportHostInteractionFailure(state.DetailsAvailability); }
    }

    internal async Task OpenObservedResultAsync(string name, Func<bool> admitted)
    {
        var state = model;
        var selected = state?.SelectedSessionRecord;
        var live = state?.CaptureNavigationEligibility();
        var foreground = CaptureObservedResultEligibility();
        bool Current() => !disposed && admitted() && state is not null && ReferenceEquals(model, state)
            && selected is not null && state.SelectedSessionRecord == selected && live!()
            && foreground();
        if (!Current() || interaction?.IsVisible != true)
        {
            main.ReportHostInteractionFailure("Observed result unavailable: the exact conversation page must be presented in the foreground.");
            return;
        }
        if (!await state!.OpenNamedObservationAsync(name, Current) && Current())
        { await interaction.SessionView.ShowAvailableDetailsAsync(state, choose: true); }
    }

    internal Func<bool> CaptureObservedResultEligibility()
    {
        var state = model;
        var selected = state?.SelectedSessionRecord;
        var source = state?.CaptureNavigationEligibility();
        var owner = interaction;
        var pane = owner?.SessionView;
        var foreground = presentation?.CaptureForegroundSourceEligibility();
        var paneRevision = pane?.ConversationPresentationRevision;
        bool Current() => !disposed && state is not null && selected is not null && owner is not null
            && ReferenceEquals(model, state) && ReferenceEquals(interaction, owner)
            && state.SelectedSessionRecord == selected && source!()
            && foreground?.Invoke() == true && pane!.ConversationPresentationRevision == paneRevision;
        var initiallyPresented = Current() && owner!.IsVisible && owner.IsActive && owner.IsSessionListPresented
            && pane!.IsConversationPresented && presentation?.HasUniqueForegroundWindow == true;
        return () => initiallyPresented && Current();
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
    internal Action? OpenRetainedAttachments { get; set; }
    internal Action<HostId<SessionIdentity>, Func<bool>>? OpenPermissionGrants { get; set; }
    internal Action? SynchronizePermissionGrants { get; set; }
    internal QuestionWindowController? ConversationQuestions { get; set; }
    internal async Task<bool> PrepareShutdown()
    {
        RetireNamedConversation();
#pragma warning disable VSTHRD003 // Join the exact owned passive navigation, not merely the choice window.
        if (!NamedConversationOperation.IsCompleted)
        {
            try { await NamedConversationOperation; }
            catch (OperationCanceledException) { }
        }
#pragma warning restore VSTHRD003
        if (instructionScopes is { } scopes) { await scopes.QuiesceAsync(); }
        return presentation is { } current ? await current.PrepareShutdown() : true;
    }
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
        if (scopeCommands is not null && scopeBroker is not null)
        {
            instructionScopes = new(main.CaptureSessionInstructionScopeEligibility, () => main.CanRevealPrivatePresentation,
                main.ReportHostInteractionFailure, scopeCommands, scopeBroker,
                () => model, () => window ?? (Avalonia.Controls.Window?)compact);
            main.BindSessionInstructionScopes(instructionScopes.ExecuteAsync, instructionScopes.RevokeAll);
        }
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
                && (presentation?.HasUniqueForegroundWindow == true
                    || active?.IsMcpActive == true && ConversationQuestions?.IsOwnerInUse == true);
            if (!Live()) { throw new InvalidOperationException("MCP commands require the exact foreground selected session and explicit native server/account choices."); }
            token.ThrowIfCancellationRequested();
            if (command.Stop) { await active!.CancelMcpConversationAsync(); return "Cancellation requested; owned work remains held until actual termination. No rollback or automatic retry."; }
            if (command.Explicit)
            {
                await active!.InvokeSelectedMcpAsync(origin, Live);
                if (!Live()) { throw new OperationCanceledException("The original MCP presentation changed."); }
                return active.ConversationStatus;
            }
            var message = await active!.SubmitMcpConversationAsync(command.Body, origin, Live);
            if (!Live()) { throw new OperationCanceledException("The original MCP presentation changed; no result is disclosed to another target."); }
            return message?.AssistantResponse?.Body ?? active.ConversationStatus;
        });
        main.BindQueueCancellationReview(this);
        main.PrivacyClosureRequested += OnPrivacyClosure;
        service.SessionLifecycleChanged += RevokeSession;
        service.SessionRetired += RevokeSession;
        service.SessionMetadataChanging += OnAnswerSourceChanging;
        service.SessionMetadataChanging += OnDictationSourceChanging;
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
            opened.FindControl<Button>("RetainedAttachments")!.IsVisible = OpenRetainedAttachments is not null;
            opened.RetainedAttachmentsRequested += (_, _) =>
                HostRequestRunner.Run(RequestOrigin.LocalUi, () =>
                {
                    if (disposed || !ReferenceEquals(window, opened) || !opened.IsVisible
                        || !access.CanInspect || !main.CanRevealPrivatePresentation || OpenRetainedAttachments is null)
                    {
                        main.ReportHostInteractionFailure("Retained attachments navigation unavailable under current private desktop ownership.");
                        return;
                    }
                    OpenRetainedAttachments();
                });
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

    private Avalonia.Controls.Window? DetailOwner()
    {
        if (CreateInteraction is null) { return window ?? (Avalonia.Controls.Window?)compact; }
        if (interaction is null)
        {
            var opened = CreateInteraction(model!, presentation!);
            interaction = opened;
            opened.Closed += (_, _) =>
            {
                if (ReferenceEquals(interaction, opened)) { interaction = null; }
                ReleaseUnusedPresentation();
            };
        }
        return interaction;
    }

    internal void PrepareDetailsHost()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        EnsurePresentation();
        _ = DetailOwner();
    }

    private void EnsurePresentation()
    {
        if (presentation is not null) { return; }
        interactionListInitialized = false;
        model = new(service, evidence, access, logger, localEvents,
            content => details?.OpenHistoryDetail(content, DetailOwner())
                ?? "History details unavailable: the native viewer is not composed.",
            session => details?.RevokeSession(session), memories, attachments, providers, conversation,
            (content, admitted, resolve, token) => details?.OpenConversationDetailAsync(content,
                DetailOwner(), admitted, resolve, token)
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
            answerReads: new SessionAnswerReadWorkflow(service, main),
            reviewPlaybackVolumeSuggestion: main.ReviewPlaybackVolumeSuggestionAsync,
            confirmPlaybackVolumeSuggestion: (review, token) => main.ConfirmPlaybackVolumeSuggestionAsync(review, token),
            localHandoff: localHandoff, retentionApply: retentionApply);
        model.BindDictation(main.ComposerDictation);
        model.BindCompletionNotices(main);
        model.PropertyChanged += OnPermissionSourceChanged;
        model.InspectPermissionGrants = (target, admitted) =>
        {
            if (disposed || !main.CanRevealPrivatePresentation || OpenPermissionGrants is null || !admitted())
            { throw new InvalidOperationException("The selected session permission surface is unavailable."); }
            var source = model;
            OpenPermissionGrants(target, () => !disposed && ReferenceEquals(model, source) && admitted());
        };
        model.PresentEvidenceDetails = (page, admitted, validate) =>
        {
            if (DetailOwner() is not ResponseWindow primary)
            { return "Session evidence Details unavailable: the primary interaction surface is not composed."; }
            primary.ShowSessions(expanded: true);
            return primary.SessionView.ShowEvidence(page, admitted, validate)
                ? "Exact bounded session evidence page opened. Diagnostics are not authorization or effect receipts."
                : "Session evidence Details unavailable: the original source, selection or display layout changed.";
        };
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
            var mcp = new McpNativeReview(questions, () => window ?? (Avalonia.Controls.Window?)compact ?? interaction
                ?? throw new InvalidOperationException("The exact conversation window is unavailable."));
            model.McpPresenter = mcp.PresentAsync;
            model.McpSelector = mcp.SelectAsync;
            model.McpInput = mcp.InputAsync;
        }
        BindAdditionalRegisteredSettingSuggestions(model, main);
        model.RemainingWorkAnswer = main.AnswerRemainingWorkAsync;
        model.RemainingWorkAdmission = () => main.CanAnswerRemainingWork;
        presentation = new(model, main);
        model.OpenSessionInstructionScopes = () =>
        {
            try
            {
                (instructionScopes ?? throw new InvalidOperationException("Session instruction scope is unavailable.")).Open();
            }
            catch (Exception exception) when (exception is InvalidOperationException)
            { main.ReportHostInteractionFailure(exception.Message); }
        };
    }

    private void ReleaseUnusedPresentation()
    {
        if (window is not null || compact is not null || interaction is not null) { return; }
        if (model is not null) { model.PropertyChanged -= OnPermissionSourceChanged; }
        model = null;
        presentation = null;
        SynchronizePermissionGrants?.Invoke();
    }

    private void OnPermissionSourceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) =>
        SynchronizePermissionGrants?.Invoke();

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
        return command.StopRunningWork
            ? await state.ReviewStopAndDeleteCommandAsync(command, origin, admitted, NativeAdmission, token)
            : await state.ReviewDispositionAsync(command, origin, admitted, NativeAdmission, token);
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
            state.RevokeSessionPresentation(session);
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
    private void OnPrivacyClosure(object? sender, EventArgs args)
    { RetireNamedConversation(); instructionScopes?.RevokeAll(); presentation?.Revoke(); }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        RetireNamedConversation();
        instructionScopes?.Retire();
        main.BindSessionInstructionScopes(null, null);
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
        service.SessionMetadataChanging -= OnDictationSourceChanging;
        presentation?.Revoke();
        if (model is not null) { model.PropertyChanged -= OnPermissionSourceChanged; }
        SynchronizePermissionGrants?.Invoke();
        SynchronizePermissionGrants = null;
        OpenPermissionGrants = null;
        window = null;
        compact = null;
        interaction = null;
        model = null;
        presentation = null;
    }
}
