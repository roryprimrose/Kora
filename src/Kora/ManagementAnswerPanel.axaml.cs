using Avalonia.Controls;

namespace Kora;

internal sealed partial class ManagementAnswerPanel : UserControl
{
    internal Task Operation { get; private set; } = Task.CompletedTask;

    public ManagementAnswerPanel()
    {
        InitializeComponent();
        ReviewManagement.Click += async (_, _) =>
        {
            if (DataContext is SessionsViewModel model) { await (Operation = model.ReviewManagementAnswerAsync()); }
        };
        ConfirmManagement.Click += async (_, _) =>
        {
            if (DataContext is SessionsViewModel model) { await (Operation = model.ConfirmManagementAnswerAsync()); }
        };
        CancelManagement.Click += (_, _) =>
        {
            if (DataContext is SessionsViewModel model) { model.CancelManagementAnswer(); }
        };
    }
}
