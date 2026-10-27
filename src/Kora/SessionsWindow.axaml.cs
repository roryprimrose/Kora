using Avalonia.Controls;
using Avalonia.Input;

using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsWindow : Window
{
    private readonly SessionsPresentationOwner owner;
    internal Task ConversationOperation { get; private set; } = Task.CompletedTask;
    internal Task ConversationCancellationOperation { get; private set; } = Task.CompletedTask;
    internal Task StopAndDeleteOperation { get; private set; } = Task.CompletedTask;
    internal Task MemoryBatchOperation { get; private set; } = Task.CompletedTask;
    internal void ClosePrivate()
    {
        owner.Revoke();
    }
    internal event EventHandler? AllWorkRequested;
    internal event EventHandler? NewRequestRequested;
    internal event EventHandler? CompactConversationRequested;
    internal event EventHandler? RetainedAttachmentsRequested;
    internal SessionsWindow(SessionsViewModel model, SessionsPresentationOwner? owner = null)
    {
        this.owner = owner ?? new(model);
        InitializeComponent();
        DataContext = model;
        BindHistoryContext(model);
        BindMixedContext(model);
        BindWorkScrolling();
        BindListPageOrder(model);
        BindInstructionContext(model);
        BindSessionInstructionScopes(model);
        _ = new NativeDraftWrapping(this, ConversationComposer, WrapDraftText, model, model.CaptureDraftPresentationEligibility);
        this.owner.Attach(this);
        CompactConversation.Click += (_, _) => CompactConversationRequested?.Invoke(this, EventArgs.Empty);
        ReadConversation.Click += async (_, _) => await (ConversationOperation = model.ReadConversationAsync());
        ReadSelectedAnswer.Click += async (_, _) => await (ConversationOperation = model.ReadSelectedAnswerAsync());
        CancelAnswerRead.Click += (_, _) => model.CancelAnswerRead();
        ReviewFailedRetry.Click += async (_, _) => await (ConversationOperation = model.ReviewFailedRetryAsync());
        ConfirmFailedRetry.Click += async (_, _) => await (ConversationOperation = model.ConfirmFailedRetryAsync());
        CancelFailedRetry.Click += (_, _) => model.CancelFailedRetry();
        Closed += (_, _) => model.CancelAnswerRead();
        NextConversation.Click += async (_, _) => await (ConversationOperation = model.ReadConversationAsync(next: true));
        AnswerAttachment.Click += async (_, _) => await (ConversationOperation = model.OpenAttachmentAnswerAsync());
        SearchConversation.Click += async (_, _) => await (ConversationOperation = model.SearchConversationAsync());
        NextConversationSearch.Click += async (_, _) => await (ConversationOperation = model.SearchConversationAsync(next: true));
        CancelConversationSearch.Click += (_, _) => model.ClearConversationSearch();
        ClearConversationSearch.Click += (_, _) => model.ClearConversationSearch();
        foreach (var field in new[] { ConversationQuery, ConversationSubmittedFrom, ConversationSubmittedThrough })
        {
            field.KeyDown += async (_, args) =>
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
        }
        RecoverConversationDraft.Click += async (_, _) => await (ConversationOperation = model.RecoverLegacyUnsentAsync());
        DiscardComposer.Click += async (_, _) => await (ConversationOperation = model.DiscardComposerBufferAsync());
        SubmitConversation.Click += async (_, _) => await (ConversationOperation = model.SubmitConversationAsync());
        SubmitLocalAnswer.Click += async (_, _) => await (ConversationOperation = model.SubmitLocalAnswerAsync());
        SubmitCopilotConversation.Click += async (_, _) => await (ConversationOperation = model.SubmitCopilotConversationAsync());
        BindClaudeConversation(model);
        SubmitConversationWithClarification.Click += async (_, _) =>
            await (ConversationOperation = model.SubmitConversationWithClarificationAsync());
        SubmitReadOnlyWithClarification.Click += async (_, _) =>
            await (ConversationOperation = model.SubmitReadOnlyWithClarificationAsync());
        SubmitWebPage.Click += async (_, _) => await (ConversationOperation = model.SubmitWebPageAsync());
        ReadMcpOptions.Click += async (_, _) => await (ConversationOperation = model.ReadMcpOptionsAsync());
        SubmitMcp.Click += async (_, _) => await (ConversationOperation = model.SubmitMcpConversationAsync());
        CancelMcp.Click += async (_, _) => await (ConversationCancellationOperation = model.CancelMcpConversationAsync());
        RefreshRunningConversation.Click += async (_, _) => await model.RefreshRunningConversationAsync();
        CancelRunningConversation.Click += async (_, _) =>
            await (ConversationCancellationOperation = model.CancelRunningConversationAsync());
        PropertyChanged += (_, args) =>
        {
            if (args.Property == IsVisibleProperty)
            {
                model.SetStopAndDeleteSurfaceOpen(IsVisible);
                model.SetRoutineQuietSurfaceOpen(IsVisible);
                model.SetProviderSurfaceOpen(IsVisible);
            }
        };
        Refresh.Click += async (_, _) => await model.RefreshAsync();
        Next.Click += async (_, _) => await model.NextAsync();
        AllWork.Click += (_, _) => AllWorkRequested?.Invoke(this, EventArgs.Empty);
        RetainedAttachments.Click += (_, _) => RetainedAttachmentsRequested?.Invoke(this, EventArgs.Empty);
        NewRequest.Click += (_, _) => NewRequestRequested?.Invoke(this, EventArgs.Empty);
        SearchList.Click += async (_, _) => await model.SearchListAsync();
        NextListSearch.Click += async (_, _) => await model.SearchListAsync(next: true);
        CancelListSearch.Click += (_, _) => model.CancelListSearch();
        ClearListSearch.Click += (_, _) => { model.ClearListSearch(); ListQuery.Focus(); };
        foreach (var field in new[] { ListQuery })
        {
            field.KeyDown += async (_, args) =>
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
        }
        Records.SelectionChanged += async (_, _) =>
        {
            await model.SelectAsync(Records.SelectedItem as SessionWorkspaceEntry);
        };
        CreateSession.Click += async (_, _) => await model.CreateAsync();
        RenameSession.Click += async (_, _) => await model.RenameAsync();
        AttachFile.Click += async (_, _) => await model.OpenAttachment(attach: true);
        InspectAttachment.Click += async (_, _) => await model.OpenAttachment(attach: false);
        RefreshProviders.Click += async (_, _) => await model.RefreshProviders();
        ReviewProviderCopy.Click += async (_, _) => await model.ReviewProviderCopy();
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
        ClearHistorySearch.Click += (_, _) => { model.ClearHistorySearchInputs(); HistoryQuery.Focus(); };
        foreach (var field in new[] { HistoryQuery })
        {
            field.KeyDown += async (_, args) =>
            {
                if (args.Key == Key.Enter && model.CanSearchHistory)
                {
                    args.Handled = true;
                    await model.SearchHistoryAsync();
                }
                else if (args.Key == Key.Escape)
                {
                    args.Handled = true;
                    model.ClearHistorySearchInputs();
                }
            };
        }
        HistoryRecords.SelectionChanged += (_, _) =>
            model.SelectHistoryRecord(HistoryRecords.SelectedItem as SessionHistoryEvent);
        OpenHistoryDetail.Click += async (_, _) => await model.OpenHistoryDetailAsync();
        WorkRecords.SelectionChanged += (_, _) =>
            model.SelectWorkRecord(WorkRecords.SelectedItem as SessionWorkRow);
        InspectWork.Click += async (_, _) => await model.InspectWorkAsync();
        RemoveQueueEntry.Click += async (_, _) => await model.RemoveQueueEntryAsync();
        PendingCancellationEntries.SelectionChanged += (_, _) =>
            model.SelectPendingCancellationEntries(PendingCancellationEntries.SelectedItems?.OfType<Kora.Core.Hosting.SessionQueueEntry>().ToArray() ?? []);
        ReviewPendingCancellation.Click += async (_, _) => await model.ReviewPendingCancellationAsync();
        ConfirmPendingCancellation.Click += async (_, _) => await model.ConfirmPendingCancellationAsync();
        DiscardPendingCancellation.Click += async (_, _) => await model.DiscardPendingCancellationAsync();
        ReadQueue.Click += async (_, _) => await model.RefreshWorkAsync();
        CancelQueueEntry.Click += async (_, _) => await model.CancelWorkAsync();
        ClearQueue.Click += async (_, _) => await model.ClearQueueAsync();
        EventRecords.SelectionChanged += (_, _) =>
            model.SelectLocalEvent(EventRecords.SelectedItem as Kora.Core.Interaction.LocalEventView);
        ClearLocalEventFilters.Click += (_, _) => model.ClearLocalEventFilters();
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
        ReviewStopAndDelete.Click += async (_, _) => await (StopAndDeleteOperation = model.ReviewStopAndDeleteAsync());
        ConfirmStopAndDelete.Click += async (_, _) => await (StopAndDeleteOperation = model.ConfirmStopAndDeleteAsync());
        CancelStopAndDelete.Click += (_, _) => model.CancelStopAndDeleteReview();
        ListMemories.Click += async (_, _) => await model.ListMemoriesAsync();
        BindMemoryContext(model);
        ProposeMemory.Click += async (_, _) => await model.ProposeMemoryAsync();
        ClearMemoryFilters.Click += (_, _) => { model.ClearMemoryFilters(); MemoryIdFilter.Focus(); };
        foreach (var filter in new Control[] { MemoryIdFilter, MemoryScopeIdentityFilter, MemoryMinimumRevisionFilter, MemoryMaximumRevisionFilter,
            MemoryCreatedFromFilter, MemoryCreatedUntilFilter, MemoryScopeFilter, MemoryReviewFilter, MemoryRetentionFilter, MemoryOrder })
        {
            filter.AddHandler(KeyDownEvent, (_, args) =>
            {
                if (args.Key == Key.Escape)
                {
                    args.Handled = true;
                    if (filter is ComboBox choice) { choice.IsDropDownOpen = false; }
                    model.ClearMemoryFilters();
                }
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }
        MemoryRecords.SelectionChanged += (_, _) =>
            model.SelectMemory(MemoryRecords.SelectedItem as Kora.Core.Commands.MemorySummary);
        InspectMemory.Click += async (_, _) => await model.InspectMemoryAsync();
        AcceptMemory.Click += async (_, _) => await model.ReviewMemoryAsync(accept: true);
        RejectMemory.Click += async (_, _) => await model.ReviewMemoryAsync(accept: false);
        AdmitMemory.Click += async (_, _) => await model.AdmitMemoryAsync();
        EditMemory.Click += async (_, _) => await model.EditMemoryAsync();
        DisableMemory.Click += async (_, _) => await model.DisableMemoryAsync();
        ForgetMemory.Click += async (_, _) => await model.ForgetMemoryAsync();
        var synchronizingMemoryBatch = false;
        MemoryBatchRecords.SelectionChanged += (_, _) =>
        {
            if (!synchronizingMemoryBatch)
            {
                model.SelectMemoryBatch(MemoryBatchRecords.SelectedItems?.OfType<Kora.Core.Commands.MemorySummary>().ToArray() ?? []);
            }
        };
        System.ComponentModel.PropertyChangedEventHandler memoryBatchChanged = (_, args) =>
        {
            if (!string.Equals(args.PropertyName, nameof(SessionsViewModel.MemoryBatchSelection), StringComparison.Ordinal)) { return; }
            synchronizingMemoryBatch = true;
            try
            {
                if (model.MemoryBatchSelection.Count == 0) { MemoryBatchRecords.SelectedItems?.Clear(); }
            }
            finally { synchronizingMemoryBatch = false; }
        };
        model.PropertyChanged += memoryBatchChanged;
        Closed += (_, _) => model.PropertyChanged -= memoryBatchChanged;
        ReviewMemoryBatch.Click += async (_, _) => await (MemoryBatchOperation = model.ReviewMemoryBatchAsync());
        ConfirmMemoryBatch.Click += async (_, _) => await (MemoryBatchOperation = model.ConfirmMemoryBatchAsync());
        CancelMemoryBatch.Click += (_, _) => model.CancelMemoryBatch();
        MemoryBatchRecords.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                args.Handled = true;
                model.CancelMemoryBatch();
            }
        };
        CloseView.Click += (_, _) => Close();
        Closed += (_, _) =>
        {
            model.SetRoutineQuietSurfaceOpen(false);
            model.SetProviderSurfaceOpen(false);
        };
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape && !args.Handled) { Close(); args.Handled = true; }
        };
    }

}