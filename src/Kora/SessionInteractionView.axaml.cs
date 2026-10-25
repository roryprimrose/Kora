using Avalonia.Controls;

using Kora.Core.Storage;

namespace Kora;

internal sealed partial class SessionInteractionView : UserControl
{
    internal event EventHandler? ManagementRequested;
    internal event EventHandler? NewConversationRequested;
    internal Task Operation { get; private set; } = Task.CompletedTask;

    public SessionInteractionView()
    {
        InitializeComponent();
        RefreshSessions.Click += async (_, _) => await Run(model => model.RefreshAsync());
        NextSessions.Click += async (_, _) => await Run(model => model.NextAsync());
        ReadConversation.Click += async (_, _) => await Run(model => model.ReadConversationAsync());
        NextConversation.Click += async (_, _) => await Run(model => model.ReadConversationAsync(next: true));
        ReadActivity.Click += async (_, _) => await Run(model => model.ReadHistoryAsync());
        NextActivity.Click += async (_, _) => await Run(model => model.ReadHistoryAsync(next: true));
        ActivityDetails.Click += async (_, _) => await Run(model => model.OpenHistoryDetailAsync());
        SubmitMessage.Click += async (_, _) => await Run(model => model.SubmitConversationAsync());
        DiscardMessage.Click += async (_, _) => await Run(model => model.DiscardComposerBufferAsync());
        SessionList.SelectionChanged += async (_, args) =>
        {
            if (args.AddedItems.Count == 1 && args.AddedItems[0] is SessionWorkspaceEntry entry)
            { await Run(model => model.SelectAsync(entry)); }
        };
        ActivityList.SelectionChanged += (_, args) =>
        {
            if (DataContext is SessionsViewModel model && args.AddedItems.Count == 1
                && args.AddedItems[0] is SessionHistoryEvent entry)
            { model.SelectHistoryRecord(entry); }
        };
        ManageSessions.Click += (_, _) => ManagementRequested?.Invoke(this, EventArgs.Empty);
        NewConversation.Click += (_, _) => NewConversationRequested?.Invoke(this, EventArgs.Empty);
    }

    private Task Run(Func<SessionsViewModel, Task> operation) =>
        Operation = DataContext is SessionsViewModel model ? operation(model) : Task.CompletedTask;
}
