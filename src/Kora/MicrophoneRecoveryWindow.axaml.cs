using Avalonia.Controls;
using Avalonia.Input;
using Kora.Application.Infrastructure;
using Kora.Application.ViewModels;
using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class MicrophoneRecoveryWindow : Window
{
    internal MicrophoneRecoveryWindow(MicrophoneRecoveryViewModel model, Action reviewSettings,
        ILogger<MicrophoneRecoveryWindow> logger)
    {
        InitializeComponent();
        DataContext = model;
        void Run(Func<Task> action)
        {
            var command = new AsyncCommand(action, exception =>
            {
                RecoveryFailed(logger, exception);
                ErrorText(exception);
            });
            command.Execute(null);
        }
        void ErrorText(Exception exception)
        {
            if (IsVisible)
            {
                Title = "Microphone recovery failed - " + exception.GetType().Name
                    + "; close and retry from the owning unlocked host";
            }
        }
        Opened += (_, _) => Run(model.RefreshAsync);
        RefreshDevices.Click += (_, _) => Run(model.RefreshAsync);
        SaveChoice.Click += (_, _) =>
        {
            var exactChoice = model.Draft;
            Run(() => model.SaveAsync(exactChoice));
        };
        EnableInput.Click += (_, _) =>
        {
            var exactSelection = model.DisplayedSelection;
            Run(() => model.EnableAsync(exactSelection));
        };
        DisableInput.Click += (_, _) => Run(model.DisableAsync);
        StopOutput.Click += (_, _) => Run(model.StopSpeakingAsync);
        ReviewSettings.Click += (_, _) => reviewSettings();
        CloseView.Click += (_, _) => Close();
        Closed += (_, _) => model.Dispose();
        KeyDown += (_, args) => { if (args.Key == Key.Escape) { Close(); args.Handled = true; } };
    }
}
