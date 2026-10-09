using System.Text.Json;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

using Kora.Core.Skills;

namespace Kora;

public sealed partial class SharedSkillSourcesWindow
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
                + "No enablement, execution or grants change. Re-registering requires a fresh native selection and a new source ID.",
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
            return await dialog.ShowDialog<bool>(this);
        }
        finally
        {
            identity.Text = string.Empty;
            dialog.Content = null;
        }
    }
}
