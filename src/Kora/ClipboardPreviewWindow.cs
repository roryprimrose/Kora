using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Kora.Application.Infrastructure;
using Kora.Core.Context;

namespace Kora;

internal sealed class ClipboardPreviewWindow(Action<Exception> reportFailure) : Window, IClipboardPreviewView
{
    private readonly TextBlock source = new() { FontFamily = new FontFamily("Consolas") };

    public void Show(ClipboardSnapshot snapshot, Func<Task> reuse, Func<Task> revoke)
    {
        Title = "Local clipboard snapshot (untrusted plain text)";
        Width = 760;
        Height = 600;
        source.Text = snapshot.Text;
        var reuseButton = new Button { Content = "Reuse this exact snapshot ID" };
        var command = new AsyncCommand(reuse, reportFailure);
        reuseButton.Command = command;
        var revokeButton = new Button { Content = "Revoke and clear" };
        revokeButton.Command = new AsyncCommand(revoke, reportFailure);
        var body = new DockPanel { Margin = new Avalonia.Thickness(16) };
        var chrome = new StackPanel { Spacing = 8 };
        chrome.Children.Add(new TextBlock
        {
            Text = $"Snapshot: {snapshot.SnapshotId:D}\nSource: {snapshot.SourceId:D} / {ClipboardSnapshot.Format}\n"
                + $"Read version: {snapshot.Version} / captured: {snapshot.CapturedAt:O} / {snapshot.Utf8Bytes} UTF-8 bytes",
        });
        chrome.Children.Add(new TextBlock
        {
            Text = "Exact local text only. May contain secrets. No inference, egress approval, copy/write or history.\n"
                + "Explanation unavailable pending qualified local tool-loop/clipboard-answering gates.\n"
                + "Clipboard changes do not update this snapshot. Close revokes it.",
            TextWrapping = TextWrapping.Wrap,
        });
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        controls.Children.Add(reuseButton);
        controls.Children.Add(revokeButton);
        chrome.Children.Add(controls);
        DockPanel.SetDock(chrome, Dock.Top);
        body.Children.Add(chrome);
        body.Children.Add(new ScrollViewer
        {
            Content = source,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        });
        Content = body;
        base.Show();
        Activate();
    }

    public void ClearAndClose()
    {
        source.Text = null;
        Content = null;
        Close();
    }
}
