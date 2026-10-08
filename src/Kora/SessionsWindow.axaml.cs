using Avalonia.Controls;
using Avalonia.Input;

using Kora.Core.Storage;
using Kora.Core.Hosting;

namespace Kora;

internal sealed partial class SessionsWindow : Window
{
    internal SessionsWindow(SessionsViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        Refresh.Click += async (_, _) => await model.RefreshAsync();
        Next.Click += async (_, _) => await model.NextAsync();
        Records.SelectionChanged += async (_, _) =>
        {
            if (model.CanRead) { await model.SelectAsync(Records.SelectedItem as SessionWorkspaceEntry); }
        };
        CreateSession.Click += async (_, _) => await model.CreateAsync();
        RenameSession.Click += async (_, _) => await model.RenameAsync();
        NextQuestions.Click += async (_, _) => await model.NextQuestionsAsync();
        NextTasks.Click += async (_, _) => await model.NextTasksAsync();
        ReadHistory.Click += async (_, _) => await model.ReadHistoryAsync();
        NextHistory.Click += async (_, _) => await model.ReadHistoryAsync(next: true);
        TaskRecords.SelectionChanged += (_, _) =>
            model.SelectTask(TaskRecords.SelectedItem is HostTaskRecord task ? task : null);
        InspectTask.Click += async (_, _) => await model.InspectTaskAsync();
        CancelTask.Click += async (_, _) => await model.CancelTaskAsync();
        QueueEntries.SelectionChanged += (_, _) =>
            model.SelectQueueEntry(QueueEntries.SelectedItem is SessionQueueEntry entry ? entry : null);
        ReadQueue.Click += async (_, _) => await model.ReadQueueAsync();
        EnqueueVersion.Click += async (_, _) => await model.EnqueueVersionAsync();
        DispatchQueue.Click += async (_, _) => await model.DispatchQueueAsync();
        CancelQueueEntry.Click += async (_, _) => await model.CancelQueueEntryAsync();
        ClearQueue.Click += async (_, _) => await model.ClearQueueAsync();
        Evidence.Click += async (_, _) => await model.ReadEvidenceAsync();
        NextEvidence.Click += async (_, _) => await model.ReadEvidenceAsync(next: true);
        Done.Click += async (_, _) => await model.ChangeLifecycleAsync(active: false);
        Resume.Click += async (_, _) => await model.ChangeLifecycleAsync(active: true);
        PreviewDisposition.Click += async (_, _) => await model.PreviewDispositionAsync();
        ConfirmDisposition.Click += async (_, _) => await model.ConfirmDispositionAsync();
        CloseView.Click += (_, _) => Close();
        Closed += (_, _) => model.Close();
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) { Close(); args.Handled = true; }
        };
    }
}
