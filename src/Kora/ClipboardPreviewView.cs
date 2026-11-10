using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

using Kora.Application.Infrastructure;
using Kora.Application.ViewModels;
using Kora.Controls;
using Kora.Core.Context;

namespace Kora;

internal sealed partial class ClipboardPreviewView : UserControl, IClipboardPreviewView
{
    private readonly Action<Exception> reportFailure;
    private readonly MainViewModel? typographyHost;
    private readonly NativeViewLifetime presentation;
    private bool closed;
    internal Window Owner { get; }
    public event EventHandler? Closed;

    internal ClipboardPreviewView(Window owner, Action<Exception> reportFailure, MainViewModel? typographyHost = null)
    {
        Owner = owner;
        this.reportFailure = reportFailure;
        this.typographyHost = typographyHost;
        presentation = new(this, owner, () => !closed && (typographyHost?.CanRevealPrivatePresentation ?? true));
        presentation.PresentationChanged += (_, _) => { if (!presentation.IsPresented) { ClearAndClose(); } };
        owner.Closed += OnOwnerClosed;
    }

    private readonly NamedTextBlock source = new() { FontFamily = new FontFamily("Consolas") };
    private readonly NamedTextBlock explanation = new()
    {
        TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    public void Show(ClipboardSnapshot snapshot, Func<Task> reuse, Func<Task> revoke, Func<bool> isCurrent,
        Func<Task>? explain = null, ClipboardQuestionActions? questions = null)
    {
        typographyHost?.EnsureReaderTypographyAvailable();
        ClearFindState();
        ClearWrappingState();
        ClearQuestion();
        if (closed) { throw new InvalidOperationException("The original clipboard view retired. Capture and review a fresh snapshot."); }
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
                + "Review and explain locally confirms one use of this complete snapshot by a qualified LOCAL runtime only; review expires 15 minutes after capture. "
                + "The clipboard tool requires a permission grant: deny, Once, This session or Always. "
                + "You are responsible for sensitive text you place in the clipboard; Kora does not classify or scan it. "
                + "A reusable grant never replaces this exact-snapshot confirmation. No hosted or bootstrap fallback.\n"
                + "Clipboard changes do not update this snapshot. Close revokes it.",
            TextWrapping = TextWrapping.Wrap,
        };
        AutomationProperties.SetName(disclosure, "Clipboard snapshot privacy and scope disclosure");
        chrome.Children.Add(disclosure);
        var controls = new WrapPanel { Orientation = Orientation.Horizontal };
        controls.Children.Add(reuseButton);
        controls.Children.Add(revokeButton);
        chrome.Children.Add(controls);
        if (explain is not null)
        {
            var consent = new CheckBox { Content = "I reviewed this exact text and confirm one local-only explanation." };
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
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(source);
        AutomationProperties.SetName(explanation, "Volatile local clipboard explanation; untrusted model response");
        content.Children.Add(explanation);
        var sourceScrollViewer = new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        if (questions is not null) { AddQuestion(content, snapshot, isCurrent, questions, sourceScrollViewer); }
        explanation.Bind(MaxWidthProperty, new Binding("Viewport.Width") { Source = sourceScrollViewer });
        AddSourceWrapping(chrome, snapshot, sourceScrollViewer);
        if (Owner is ClipboardPreviewWindow)
        {
            DockPanel.SetDock(chrome, Dock.Top);
            body.Children.Add(chrome);
            body.Children.Add(sourceScrollViewer);
            Content = body;
        }
        else
        {
            var layout = new Grid { Margin = body.Margin, RowDefinitions = new("2*,3*") };
            layout.Children.Add(new ScrollViewer { Content = chrome });
            Grid.SetRow(sourceScrollViewer, 1);
            layout.Children.Add(sourceScrollViewer);
            Content = layout;
        }
        if (Owner is ResponseWindow primary)
        {
            primary.ShowSessions(expanded: true, preservePresentation: true);
            primary.ShowNativeReview(this, "Local clipboard snapshot", this, isCurrent,
                shown => { if (!shown) { ClearAndClose(); } });
        }
        else { Owner.Show(); }
        presentation.Refresh();
        Owner.Activate();
    }

    public void SetExplanation(string? text)
    {
        RefreshQuestion();
        explanation.Text = text;
    }

    public void ClearAndClose()
    {
        if (closed) { return; }
        closed = true;
        ClearFindState();
        ClearQuestion();
        source.Text = null;
        ClearWrappingState();
        explanation.Text = null;
        Content = null;
        Owner.Closed -= OnOwnerClosed;
        presentation.Dispose();
        Closed?.Invoke(this, EventArgs.Empty);
        if (Owner is ResponseWindow primary)
        { Avalonia.Threading.Dispatcher.UIThread.Post(() => primary.RemoveNativeReview(this)); }
        else if (Owner is ClipboardPreviewWindow) { Owner.Close(); }
    }

    private void OnOwnerClosed(object? sender, EventArgs args) => ClearAndClose();

    internal void ForwardRootKey(Avalonia.Input.KeyEventArgs args) => OnFindKeyDown(this, args);
}
