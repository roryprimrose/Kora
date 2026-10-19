using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using Kora.Application.Infrastructure;
using Kora.Application.ViewModels;
using Kora.Controls;
using Kora.Core.Context;

namespace Kora;

internal sealed partial class ClipboardPreviewWindow(Action<Exception> reportFailure, MainViewModel? typographyHost = null) : Window, IClipboardPreviewView
{
    private readonly NamedTextBlock source = new() { FontFamily = new FontFamily("Consolas") };
    private readonly NamedTextBlock explanation = new() { TextWrapping = TextWrapping.Wrap };

    public void Show(ClipboardSnapshot snapshot, Func<Task> reuse, Func<Task> revoke, Func<bool> isCurrent, Func<Task>? explain = null)
    {
        typographyHost?.EnsureReaderTypographyAvailable();
        ClearFindState();
        Title = "Local clipboard snapshot (untrusted plain text)";
        Width = 760;
        Height = 600;
        source.Text = snapshot.Text;
        if (typographyHost is not null) { NativeReaderTypography.Bind(source, typographyHost); }
        AutomationProperties.SetName(source, "Clipboard snapshot plain text content");
        var reuseButton = new Button { Content = "Reuse this exact snapshot ID" };
        AutomationProperties.SetName(reuseButton, "Reuse this exact clipboard snapshot ID");
        var command = new AsyncCommand(reuse, reportFailure);
        reuseButton.Command = command;
        var revokeButton = new Button { Content = "Revoke and clear" };
        AutomationProperties.SetName(revokeButton, "Revoke and clear this clipboard snapshot");
        revokeButton.Command = new AsyncCommand(revoke, reportFailure);
        var body = new DockPanel { Margin = new Avalonia.Thickness(16) };
        var chrome = new StackPanel { Spacing = 8 };
        var identity = new NamedTextBlock
        {
            Text = $"Snapshot: {snapshot.SnapshotId:D}\nSource: {snapshot.SourceId:D} / {ClipboardSnapshot.Format}\n"
                + $"Read version: {snapshot.Version} / captured: {snapshot.CapturedAt:O} / {snapshot.Utf8Bytes} UTF-8 bytes",
        };
        AutomationProperties.SetName(identity, "Clipboard snapshot identity and capture details");
        chrome.Children.Add(identity);
        var disclosure = new NamedTextBlock
        {
            Text = "Exact local text only. May contain secrets. No inference without explicit review, egress approval, copy/write or history.\n"
                + "Review and explain locally admits this complete snapshot to a qualified LOCAL runtime only; review expires 15 minutes after capture. "
                + "Structural credential screening is best-effort, not a secret-free guarantee. No hosted or bootstrap fallback.\n"
                + "Clipboard changes do not update this snapshot. Close revokes it.",
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetName(disclosure, "Clipboard snapshot privacy and scope disclosure");
        chrome.Children.Add(disclosure);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        controls.Children.Add(reuseButton);
        controls.Children.Add(revokeButton);
        chrome.Children.Add(controls);
        if (explain is not null)
        {
            var consent = new CheckBox { Content = "I reviewed this exact text and admit it for local-only explanation." };
            AutomationProperties.SetName(consent, "Admit this exact clipboard snapshot to local-only inference");
            var explainButton = new Button { Content = "Review and explain locally" };
            AutomationProperties.SetName(explainButton, "Review and explain this exact clipboard snapshot locally");
            explainButton.Command = new AsyncCommand(async () =>
            {
                if (!isCurrent() || consent.IsChecked != true)
                { throw new InvalidOperationException("Review and explicitly admit the exact current snapshot first."); }
                consent.IsChecked = false;
                await explain();
            }, reportFailure);
            chrome.Children.Add(consent);
            chrome.Children.Add(explainButton);
        }
        AddFind(chrome, snapshot, isCurrent);
        DockPanel.SetDock(chrome, Dock.Top);
        body.Children.Add(chrome);
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(source);
        AutomationProperties.SetName(explanation, "Volatile local clipboard explanation; untrusted model response");
        content.Children.Add(explanation);
        body.Children.Add(new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        });
        Content = body;
        Closed += OnFindClosed;
        base.Show();
        Activate();
    }

    public void SetExplanation(string? text) => explanation.Text = text;

    public void ClearAndClose()
    {
        ClearFindState();
        source.Text = null;
        explanation.Text = null;
        Content = null;
        Close();
    }

    private void OnFindClosed(object? sender, EventArgs args)
    {
        Closed -= OnFindClosed;
        ClearFindState();
        source.Text = null;
        explanation.Text = null;
        Content = null;
    }
}
