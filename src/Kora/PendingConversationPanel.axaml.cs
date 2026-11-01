using Avalonia.Controls;

namespace Kora;

internal sealed partial class PendingConversationPanel : UserControl
{
    internal Task Operation { get; private set; } = Task.CompletedTask;

    public PendingConversationPanel()
    {
        InitializeComponent();
        RefreshPending.Click += async (_, _) =>
        {
            if (DataContext is SessionsViewModel model) { await (Operation = model.RefreshPendingConversationAsync()); }
        };
        CancelPending.Click += async (_, _) =>
        {
            if (DataContext is SessionsViewModel model) { await (Operation = model.CancelPendingConversationAsync()); }
        };
        ResumePending.Click += async (_, _) =>
        {
            if (DataContext is SessionsViewModel model) { await (Operation = model.ResumePendingConversationAsync()); }
        };
        HoldDispatch.Click += async (_, _) =>
        {
            if (DataContext is SessionsViewModel model) { await (Operation = model.HoldConversationDispatchAsync()); }
        };
        ReleaseDispatch.Click += async (_, _) =>
        {
            if (DataContext is SessionsViewModel model) { await (Operation = model.ReleaseConversationDispatchAsync()); }
        };
    }
}
