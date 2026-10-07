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
        TaskRecords.SelectionChanged += (_, _) =>
            model.SelectTask(TaskRecords.SelectedItem is HostTaskRecord task ? task : null);
        InspectTask.Click += async (_, _) => await model.InspectTaskAsync();
        CancelTask.Click += async (_, _) => await model.CancelTaskAsync();
        Evidence.Click += async (_, _) => await model.ReadEvidenceAsync();
        NextEvidence.Click += async (_, _) => await model.ReadEvidenceAsync(next: true);
        Done.Click += async (_, _) => await model.ChangeLifecycleAsync(active: false);
        Resume.Click += async (_, _) => await model.ChangeLifecycleAsync(active: true);
        CloseView.Click += (_, _) => Close();
        Closed += (_, _) => model.Close();
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) { Close(); args.Handled = true; }
        };
    }
}
