using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Kora.Core.Maintenance;

namespace Kora;

internal sealed partial class MaintenanceWindowController
{
    private Window? installerWindow;

    private async Task<InstallerStartOutcome> RequestInstallerAsync(ReleaseCheck target, Func<bool> admitted, CancellationToken token)
    {
        if (installerWindow is not null) { throw new InvalidOperationException("An exact installer review is already pending."); }
        bool Current() => !disposed && ownsDesktop() && main.CanRunMaintenanceCommands
            && main.CanRevealPrivatePresentation && !main.IsProtectedCall && admitted() && !token.IsCancellationRequested;
        if (!Current()) { throw new InvalidOperationException("Native installer review is not admitted."); }
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var completion = new TaskCompletionSource<InstallerStartOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var unsigned = new CheckBox { Content = "I approve downloading and running this exact unsigned installer", IsChecked = false };
        var start = new Button { Content = "Download and run reviewed installer", IsEnabled = false };
        var cancel = new Button { Content = "Cancel" };
        var content = new StackPanel
        {
            Margin = new(24), Spacing = 14,
            Children =
            {
                new TextBlock { Text = "Review Kora " + target.Release!.Version, FontSize = 22 },
                new TextBlock { Text = state.ReleaseDetails + "\n" + state.UpgradeDisclosure, TextWrapping = TextWrapping.Wrap },
                unsigned,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { start, cancel } },
            },
        };
        var review = installerWindow = new Window
        {
            Title = "Kora exact installer approval", Width = 760, Height = 440, Content = content,
        };
        var running = false;
        unsigned.IsCheckedChanged += (_, _) => start.IsEnabled = !running && unsigned.IsChecked == true && Current();
        cancel.Click += (_, _) => { lifetime.Cancel(); review.Close(); };
        review.Closed += (_, _) =>
        {
            lifetime.Cancel();
            if (!running) { completion.TrySetResult(InstallerStartOutcome.Denied); }
        };
        start.Click += async (_, _) =>
        {
            if (running) { return; }
            if (unsigned.IsChecked != true || !Current())
            {
                main.ReportHostInteractionFailure("The exact installer approval or maintenance admission changed. No installer was started.");
                completion.TrySetResult(InstallerStartOutcome.Denied);
                review.Close();
                return;
            }
            running = true;
            start.IsEnabled = false;
            try
            {
                await state.ReviewAsync();
                if (!Current()) { throw new OperationCanceledException("Installer review changed."); }
                state.UpgradeApproved = true;
                var result = await state.UpgradeAsync(Current, lifetime.Token);
                completion.TrySetResult(result);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                main.ReportHostInteractionFailure("Installer startup was not confirmed. " + exception.GetType().Name);
                completion.TrySetException(exception);
            }
            finally { review.Close(); }
        };
        using var cancellation = token.Register(() => Dispatcher.UIThread.Post(() => review.Close()));
        try
        {
            review.Show();
            return await completion.Task;
        }
        finally
        {
            review.Close();
            review.Content = null;
            if (ReferenceEquals(installerWindow, review)) { installerWindow = null; }
        }
    }
}
