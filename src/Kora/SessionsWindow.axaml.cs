using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsWindow : Window
{
    private bool privateClose;
    private bool choosingDraft;
    private Window? draftDialog;
    private readonly SessionsViewModel model;
    internal Task ConversationOperation { get; private set; } = Task.CompletedTask;
    internal void ClosePrivate()
    {
        privateClose = true;
        draftDialog?.Close();
        model.Close();
        Close();
    }
    internal event EventHandler? AllWorkRequested;
    internal event EventHandler? NewRequestRequested;
    internal SessionsWindow(SessionsViewModel model)
    {
        this.model = model;
        InitializeComponent();
        DataContext = model;
        model.DecideComposerDraft = ChooseDraft;
        ReadConversation.Click += async (_, _) => await (ConversationOperation = model.ReadConversationAsync());
        NextConversation.Click += async (_, _) => await (ConversationOperation = model.ReadConversationAsync(next: true));
        SearchConversation.Click += async (_, _) => await (ConversationOperation = model.SearchConversationAsync());
        NextConversationSearch.Click += async (_, _) => await (ConversationOperation = model.SearchConversationAsync(next: true));
        CancelConversationSearch.Click += (_, _) => model.ClearConversationSearch();
        ClearConversationSearch.Click += (_, _) => model.ClearConversationSearch();
        ConversationQuery.KeyDown += async (_, args) =>
        {
            if (args.Key == Key.Enter && model.CanSearchConversation)
            {
                args.Handled = true;
                await (ConversationOperation = model.SearchConversationAsync());
            }
            else if (args.Key == Key.Escape)
            {
                args.Handled = true;
                model.ClearConversationSearch();
            }
        };
        SaveConversationDraft.Click += async (_, _) => await (ConversationOperation = model.SaveConversationDraftAsync());
        ReloadConversationDraft.Click += async (_, _) => await (ConversationOperation = model.ReloadConversationDraftAsync());
        DiscardComposer.Click += (_, _) => model.DiscardComposerBuffer();
        ReviewClearConversationDraft.Click += (_, _) => model.ReviewClearConversationDraft();
        ClearConversationDraft.Click += async (_, _) => await (ConversationOperation = model.ClearSavedConversationDraftAsync());
        SubmitConversation.Click += async (_, _) => await (ConversationOperation = model.SubmitConversationAsync());
        Closing += async (_, args) =>
        {
            if (privateClose || !model.ComposerDirty) { return; }
            args.Cancel = true;
            if (choosingDraft || !model.CanRead) { return; }
            choosingDraft = true;
            try
            {
                if (await model.ProtectComposerAsync()) { privateClose = true; Close(); }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            { model.ReportComposerCloseFailure(); }
            finally { choosingDraft = false; }
        };
        PropertyChanged += (_, args) =>
        {
            if (args.Property == IsVisibleProperty)
            {
                model.SetRoutineQuietSurfaceOpen(IsVisible);
                model.SetProviderSurfaceOpen(IsVisible);
            }
        };
        Refresh.Click += async (_, _) => await model.RefreshAsync();
        Next.Click += async (_, _) => await model.NextAsync();
        AllWork.Click += (_, _) => AllWorkRequested?.Invoke(this, EventArgs.Empty);
        NewRequest.Click += (_, _) => NewRequestRequested?.Invoke(this, EventArgs.Empty);
        SearchList.Click += async (_, _) => await model.SearchListAsync();
        NextListSearch.Click += async (_, _) => await model.SearchListAsync(next: true);
        CancelListSearch.Click += (_, _) => model.CancelListSearch();
        ClearListSearch.Click += (_, _) => { model.ClearListSearch(); ListQuery.Focus(); };
        ListQuery.KeyDown += async (_, args) =>
        {
            if (args.Key == Key.Enter && model.CanSearchList)
            {
                args.Handled = true;
                await model.SearchListAsync();
            }
            else if (args.Key == Key.Escape)
            {
                args.Handled = true;
                model.ClearListSearch();
            }
        };
        Records.SelectionChanged += async (_, _) =>
        {
            await model.SelectAsync(Records.SelectedItem as SessionWorkspaceEntry);
        };
        CreateSession.Click += async (_, _) => await model.CreateAsync();
        RenameSession.Click += async (_, _) => await model.RenameAsync();
        AttachFile.Click += async (_, _) => await model.OpenAttachment(attach: true);
        InspectAttachment.Click += async (_, _) => await model.OpenAttachment(attach: false);
        RefreshProviders.Click += async (_, _) => await model.RefreshProviders();
        ReviewProviderService.Click += async (_, _) => await model.ReviewProviderField(Kora.Core.Providers.SessionProviderField.Service, reset: false);
        ReviewProviderModel.Click += async (_, _) => await model.ReviewProviderField(Kora.Core.Providers.SessionProviderField.Model, reset: false);
        ReviewProviderReasoning.Click += async (_, _) => await model.ReviewProviderField(Kora.Core.Providers.SessionProviderField.Reasoning, reset: false);
        ReviewProviderContext.Click += async (_, _) => await model.ReviewProviderField(Kora.Core.Providers.SessionProviderField.Context, reset: false);
        ReviewProviderTuple.Click += async (_, _) => await model.ReviewProviderField(null, reset: false);
        ResetProviderService.Click += async (_, _) => await model.ReviewProviderField(Kora.Core.Providers.SessionProviderField.Service, reset: true);
        ResetProviderModel.Click += async (_, _) => await model.ReviewProviderField(Kora.Core.Providers.SessionProviderField.Model, reset: true);
        ResetProviderReasoning.Click += async (_, _) => await model.ReviewProviderField(Kora.Core.Providers.SessionProviderField.Reasoning, reset: true);
        ResetProviderContext.Click += async (_, _) => await model.ReviewProviderField(Kora.Core.Providers.SessionProviderField.Context, reset: true);
        ResetProviders.Click += async (_, _) => await model.ReviewProviderField(null, reset: true);
        ConfirmProviders.Click += async (_, _) => await model.ConfirmProviders();
        CancelProviders.Click += (_, _) => model.ClearProviderReview();
        RefreshRetention.Click += async (_, _) => await model.RefreshRetentionAsync();
        KeepSession.Click += (_, _) => model.PreviewRetention(keep: true);
        OrdinaryRetention.Click += (_, _) => model.PreviewRetention(keep: false);
        ConfirmRetention.Click += async (_, _) => await model.ConfirmRetentionAsync();
        CancelRetentionReview.Click += (_, _) => model.CancelRetentionReview();
        NextQuestions.Click += async (_, _) => await model.NextQuestionsAsync();
        NextTasks.Click += async (_, _) => await model.NextTasksAsync();
        ReadHistory.Click += async (_, _) => await model.ReadHistoryAsync();
        ReadExactRetention.Click += async (_, _) => await model.ReadExactRetentionAsync();
        NextHistory.Click += async (_, _) => await model.ReadHistoryAsync(next: true);
        SearchHistory.Click += async (_, _) => await model.SearchHistoryAsync();
        NextHistorySearch.Click += async (_, _) => await model.SearchHistoryAsync(next: true);
        CancelHistorySearch.Click += (_, _) => model.CancelHistorySearch();
        ClearHistorySearch.Click += (_, _) => { model.HistoryQuery = string.Empty; HistoryQuery.Focus(); };
        HistoryQuery.KeyDown += async (_, args) =>
        {
            if (args.Key == Key.Enter && model.CanSearchHistory)
            {
                args.Handled = true;
                await model.SearchHistoryAsync();
            }
            else if (args.Key == Key.Escape)
            {
                args.Handled = true;
                model.HistoryQuery = string.Empty;
            }
        };
        HistoryRecords.SelectionChanged += (_, _) =>
            model.SelectHistoryRecord(HistoryRecords.SelectedItem as SessionHistoryEvent);
        OpenHistoryDetail.Click += async (_, _) => await model.OpenHistoryDetailAsync();
        WorkRecords.SelectionChanged += (_, _) =>
            model.SelectWorkRecord(WorkRecords.SelectedItem as SessionWorkRow);
        InspectWork.Click += async (_, _) => await model.InspectWorkAsync();
        RemoveQueueEntry.Click += async (_, _) => await model.RemoveQueueEntryAsync();
        MoveQueueUp.Click += async (_, _) => await model.ReviewQueueMoveUpAsync();
        MoveQueueDown.Click += async (_, _) => await model.ReviewQueueMoveDownAsync();
        ConfirmQueueMove.Click += async (_, _) => await model.ConfirmQueueMoveAsync();
        CancelQueueMove.Click += async (_, _) => await model.CancelQueueMoveAsync();
        ReviewExpiredRequeue.Click += async (_, _) => await model.ReviewExpiredRequeueAsync();
        ConfirmExpiredRequeue.Click += async (_, _) => await model.ConfirmExpiredRequeueAsync();
        DiscardExpiredRequeue.Click += async (_, _) => await model.DiscardExpiredRequeueAsync();
        PendingCancellationEntries.SelectionChanged += (_, _) =>
            model.SelectPendingCancellationEntries(PendingCancellationEntries.SelectedItems?.OfType<Kora.Core.Hosting.SessionQueueEntry>().ToArray() ?? []);
        ReviewPendingCancellation.Click += async (_, _) => await model.ReviewPendingCancellationAsync();
        ConfirmPendingCancellation.Click += async (_, _) => await model.ConfirmPendingCancellationAsync();
        DiscardPendingCancellation.Click += async (_, _) => await model.DiscardPendingCancellationAsync();
        ReadQueue.Click += async (_, _) => await model.RefreshWorkAsync();
        NoQueuePrerequisite.Click += (_, _) => model.ChooseNoQueuePrerequisite();
        QueuePrerequisites.SelectionChanged += (_, _) =>
            model.SelectQueuePrerequisite(QueuePrerequisites.SelectedItem as QueuePrerequisiteOption);
        EnqueueVersion.Click += async (_, _) => await model.EnqueueVersionAsync();
        EnqueueCapabilities.Click += async (_, _) => await model.EnqueueCapabilitiesAsync();
        EnqueueReadiness.Click += async (_, _) => await model.EnqueueReadinessAsync();
        EnqueueRuntimes.Click += async (_, _) => await model.EnqueueRuntimesAsync();
        QueueCapabilities.SelectionChanged += (_, _) =>
            model.SelectQueueCapability(QueueCapabilities.SelectedItem as QueueCapabilityOption);
        EnqueueCapability.Click += async (_, _) => await model.EnqueueCapabilityAsync();
        EnqueueRuntimeStatus.Click += async (_, _) => await model.EnqueueRuntimeStatusAsync();
        DispatchQueue.Click += async (_, _) => await model.DispatchQueueAsync();
        PauseQueue.Click += async (_, _) => await model.PauseQueueAsync();
        ResumeQueue.Click += async (_, _) => await model.ResumeQueueAsync();
        CancelQueueEntry.Click += async (_, _) => await model.CancelWorkAsync();
        ClearQueue.Click += async (_, _) => await model.ClearQueueAsync();
        EventRecords.SelectionChanged += (_, _) =>
            model.SelectLocalEvent(EventRecords.SelectedItem as Kora.Core.Interaction.LocalEventView);
        ReviewLocalEvent.Click += async (_, _) => await model.ReviewLocalEventAsync();
        DismissLocalEvent.Click += async (_, _) => await model.DismissLocalEventAsync();
        DeferLocalEvent.Click += async (_, _) => await model.DeferLocalEventAsync();
        QuietRoutineNotices.Click += async (_, _) => await model.QuietRoutineNoticesAsync();
        ClearRoutineQuiet.Click += async (_, _) => await model.ClearRoutineQuietAsync();
        ResetRoutineQuiet.Click += async (_, _) => await model.ResetRoutineQuietAsync();
        Evidence.Click += async (_, _) => await model.ReadEvidenceAsync();
        NextEvidence.Click += async (_, _) => await model.ReadEvidenceAsync(next: true);
        Done.Click += async (_, _) => await model.ChangeLifecycleAsync(active: false);
        Resume.Click += async (_, _) => await model.ChangeLifecycleAsync(active: true);
        PreviewDisposition.Click += async (_, _) => await model.PreviewDispositionAsync();
        ConfirmDisposition.Click += async (_, _) => await model.ConfirmDispositionAsync();
        ListMemories.Click += async (_, _) => await model.ListMemoriesAsync();
        ProposeMemory.Click += async (_, _) => await model.ProposeMemoryAsync();
        MemoryRecords.SelectionChanged += (_, _) =>
            model.SelectMemory(MemoryRecords.SelectedItem as Kora.Core.Commands.MemorySummary);
        InspectMemory.Click += async (_, _) => await model.InspectMemoryAsync();
        AcceptMemory.Click += async (_, _) => await model.ReviewMemoryAsync(accept: true);
        RejectMemory.Click += async (_, _) => await model.ReviewMemoryAsync(accept: false);
        AdmitMemory.Click += async (_, _) => await model.AdmitMemoryAsync();
        EditMemory.Click += async (_, _) => await model.EditMemoryAsync();
        DisableMemory.Click += async (_, _) => await model.DisableMemoryAsync();
        ForgetMemory.Click += async (_, _) => await model.ForgetMemoryAsync();
        CloseView.Click += (_, _) => Close();
        var refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        async void OnRefreshTick(object? sender, EventArgs args)
        {
            if (model.CanRefreshWork) { await model.RefreshWorkAsync(); }
        }
        refresh.Tick += OnRefreshTick;
        Opened += (_, _) => refresh.Start();
        Closed += (_, _) =>
        {
            refresh.Stop();
            refresh.Tick -= OnRefreshTick;
            model.Close();
            model.DecideComposerDraft = null;
            draftDialog?.Close();
        };
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) { Close(); args.Handled = true; }
        };
    }

    private async Task<ComposerDraftDecision> ChooseDraft()
    {
        if (!IsVisible || draftDialog is not null) { return ComposerDraftDecision.Cancel; }
        var dialog = new Window { Title = "Unsaved exact-session conversation draft", Width = 680, Height = 230 };
        var content = new StackPanel { Margin = new(18), Spacing = 14 };
        content.Children.Add(new TextBlock
        {
            Text = model.ComposerTarget + "\nSave explicitly, discard the memory-only buffer, or cancel switching/closing. Nothing submits a message.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12 };
        foreach (var decision in Enum.GetValues<ComposerDraftDecision>())
        {
            var button = new Button { Content = decision.ToString() };
            button.Click += (_, _) => dialog.Close(decision);
            buttons.Children.Add(button);
        }
        content.Children.Add(buttons);
        dialog.Content = content;
        draftDialog = dialog;
        try { return await dialog.ShowDialog<ComposerDraftDecision?>(this) ?? ComposerDraftDecision.Cancel; }
        finally
        {
            content.Children.Clear();
            if (ReferenceEquals(draftDialog, dialog)) { draftDialog = null; }
        }
    }
}