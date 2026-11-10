using System.Text.Json;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

using Kora.Core.Skills;

namespace Kora;

internal sealed partial class SharedSkillSourcesView
{
    internal async Task<bool> ConfirmWithdrawalAsync(SharedSkillSource source, CancellationToken token)
    {
        var confirm = new Button { Content = "Unregister source: withdraw local read consent" };
        var cancel = new Button { Content = "Cancel" };
        AutomationProperties.SetName(confirm, "Confirm withdrawal for this exact registered identity");
        var identity = new TextBlock
        {
            Text = $"Source ID: {source.Id:N}\nProfile-relative root: {JsonSerializer.Serialize(source.ProfileRelativeRoot)}\n"
                + $"Directory identity: {source.DirectoryIdentity}\n\n"
                + "Remove only Kora's local registration and inspection snapshots. Your shared skill files are untouched. "
                + "Existing volatile shared-skill reviews are retired, including enablement and pending reference-read consent. "
                + "No execution or grants are created or transferred. Re-registering requires a fresh native selection, "
                + "a new source ID and separate review and enablement.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };
        AutomationProperties.SetName(identity, "Exact registered source and withdrawal effect");
        var panel = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 12,
            Children =
            {
                identity,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { confirm, cancel } },
            },
        };
        if (Owner is ResponseWindow primary)
        {
            var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            confirm.Click += (_, _) => decision.TrySetResult(true);
            cancel.Click += (_, _) => decision.TrySetResult(false);
            var admitted = CapturePresentation();
            using var withdrawn = token.Register(() => Dispatcher.UIThread.Post(() => decision.TrySetResult(false)));
            try
            {
                token.ThrowIfCancellationRequested();
                primary.ShowNativeReview(panel, "Withdraw this shared skill source's read consent", panel,
                    admitted, shown => { if (!shown) { decision.TrySetResult(false); } });
                return await decision.Task && admitted() && !token.IsCancellationRequested;
            }
            finally { primary.RemoveNativeReview(panel); identity.Text = string.Empty; panel.Children.Clear(); }
        }
        var dialog = new Window
        {
            Title = "Withdraw local read consent", Width = 640, SizeToContent = SizeToContent.Height,
            CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel,
        };
        confirm.Click += (_, _) => dialog.Close(true);
        cancel.Click += (_, _) => dialog.Close(false);
        using var cancellation = token.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(false)));
        try
        {
            token.ThrowIfCancellationRequested();
            return await dialog.ShowDialog<bool>(Owner);
        }
        finally
        {
            identity.Text = string.Empty;
            dialog.Content = null;
        }
    }
}
