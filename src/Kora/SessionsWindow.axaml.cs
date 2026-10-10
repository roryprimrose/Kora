using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionsWindow : Window
{
    internal SessionsWindow(SessionsViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        Refresh.Click += async (_, _) => await model.RefreshAsync();
        Next.Click += async (_, _) => await model.NextAsync();
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
        ReadQueue.Click += async (_, _) => await model.RefreshWorkAsync();
        EnqueueVersion.Click += async (_, _) => await model.EnqueueVersionAsync();
        DispatchQueue.Click += async (_, _) => await model.DispatchQueueAsync();
        CancelQueueEntry.Click += async (_, _) => await model.CancelWorkAsync();
        ClearQueue.Click += async (_, _) => await model.ClearQueueAsync();
        EventRecords.SelectionChanged += (_, _) =>
            model.SelectLocalEvent(EventRecords.SelectedItem as Kora.Core.Interaction.LocalEventView);
        ReviewLocalEvent.Click += async (_, _) => await model.ReviewLocalEventAsync();
        DismissLocalEvent.Click += async (_, _) => await model.DismissLocalEventAsync();
        DeferLocalEvent.Click += async (_, _) => await model.DeferLocalEventAsync();
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
        };
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) { Close(); args.Handled = true; }
        };
    }
}