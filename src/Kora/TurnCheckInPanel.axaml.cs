using Avalonia.Controls;

namespace Kora;

internal sealed partial class TurnCheckInPanel : UserControl
{
    internal Task Operation { get; private set; } = Task.CompletedTask;

    public TurnCheckInPanel()
    {
        InitializeComponent();
        KeepGoing.Click += async (_, _) =>
        {
            if (DataContext is SessionsViewModel model) { await (Operation = model.KeepGoingCheckInAsync()); }
        };
        KeepGoingFor.Click += async (_, _) =>
        {
            if (DataContext is SessionsViewModel model) { await (Operation = model.KeepGoingForCheckInAsync()); }
        };
        KeepRunning.Click += async (_, _) =>
        {
            if (DataContext is SessionsViewModel model) { await (Operation = model.KeepRunningCheckInAsync()); }
        };
        StopTurn.Click += async (_, _) =>
        {
            if (DataContext is SessionsViewModel model) { await (Operation = model.StopCheckInAsync()); }
        };
    }
}
