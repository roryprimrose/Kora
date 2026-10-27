using Avalonia.Controls;

namespace Kora;

internal sealed partial class McpResultFollowUpControls : UserControl
{
    internal Task Operation { get; private set; } = Task.CompletedTask;

    public McpResultFollowUpControls()
    {
        InitializeComponent();
#pragma warning disable VSTHRD101 // Framework Click/detach boundaries; model operations explicitly log and present failures.
        ReviewMcpFollowUp.Click += async (_, _) =>
        {
            if (DataContext is SessionsViewModel model) { await (Operation = model.ReviewMcpResultFollowUpAsync()); }
        };
        ConfirmMcpFollowUp.Click += async (_, _) =>
        {
            if (DataContext is SessionsViewModel model) { await (Operation = model.ConfirmMcpResultFollowUpAsync()); }
        };
        CancelMcpFollowUp.Click += async (_, _) =>
        {
            if (DataContext is SessionsViewModel model) { await (Operation = model.CancelMcpResultFollowUpAsync()); }
        };
        DetachedFromVisualTree += async (_, _) =>
        {
            if (DataContext is SessionsViewModel model) { await (Operation = model.CloseMcpResultFollowUpAsync()); }
        };
#pragma warning restore VSTHRD101
    }
}
