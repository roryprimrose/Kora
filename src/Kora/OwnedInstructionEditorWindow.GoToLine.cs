using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;

using Kora.Core.Artifacts;

namespace Kora;

internal sealed partial class OwnedInstructionEditorWindow
{
    internal readonly Button GoToLineButton = new() { Content = "Go to line", Margin = new(3) };
    internal readonly TextBox LineInput = new() { Width = 120, ContextMenu = null };
    internal readonly Button LineGo = new() { Content = "Go", Margin = new(3) };
    internal readonly Button LineCancel = new() { Content = "Cancel", Margin = new(3) };
    private readonly TextBlock lineStatus = new();
    private readonly StackPanel linePane = new() { Orientation = Orientation.Horizontal, IsVisible = false };
    private Func<Func<bool>?>? captureLineSource;
    private Func<bool>? lineCurrent;
    private OwnedDraftLineIndex? lineIndex;
    private bool lineGesture;
    private long lineEpoch;

    internal void BindGoToLine(Func<Func<bool>?> capture) => captureLineSource = capture;

    private void AddGoToLine(DockPanel panel, Panel controls)
    {
        AutomationProperties.SetName(GoToLineButton, "Go to line in the current displayed owned Instruction or Prompt source only (Ctrl+G)");
        AutomationProperties.SetName(LineInput, "One-based logical line number in the current displayed owned source only; ASCII positive integer, no leading zeros");
        AutomationProperties.SetName(LineGo, "Go to the entered line in the current displayed source only");
        AutomationProperties.SetName(LineCancel, "Cancel Go to line without navigating");
        AutomationProperties.SetLiveSetting(lineStatus, AutomationLiveSetting.Polite);
        LineInput.AddHandler(TextBox.CopyingToClipboardEvent, BlockLineClipboard, RoutingStrategies.Tunnel | RoutingStrategies.Bubble | RoutingStrategies.Direct);
        LineInput.AddHandler(TextBox.CuttingToClipboardEvent, BlockLineClipboard, RoutingStrategies.Tunnel | RoutingStrategies.Bubble | RoutingStrategies.Direct);
        LineInput.AddHandler(TextBox.PastingFromClipboardEvent, BlockLineClipboard, RoutingStrategies.Tunnel | RoutingStrategies.Bubble | RoutingStrategies.Direct);
        controls.Children.Add(GoToLineButton);
        linePane.Children.Add(LineInput);
        linePane.Children.Add(LineGo);
        linePane.Children.Add(LineCancel);
        linePane.Children.Add(lineStatus);
        DockPanel.SetDock(linePane, Dock.Top);
        panel.Children.Add(linePane);
        GoToLineButton.Click += (_, _) => BeginGoToLine();
        LineGo.Click += (_, _) => GoToEnteredLine();
        LineCancel.Click += (_, _) => CancelGoToLine();
        AddHandler(KeyDownEvent, (_, key) =>
        {
            if (key.Key == Key.G && key.KeyModifiers == KeyModifiers.Control)
            { key.Handled = true; BeginGoToLine(); }
            else if (linePane.IsVisible && key.Key == Key.Escape)
            { key.Handled = true; CancelGoToLine(); }
        }, RoutingStrategies.Tunnel);
        foreach (var input in new[] { Source, NameInput, CopyNameInput })
        {
            input.PropertyChanged += (_, change) =>
            {
                if (change.Property == TextBox.TextProperty || change.Property == TextBox.IsReadOnlyProperty)
                { InvalidateGoToLine(); }
            };
        }
        Files.SelectionChanged += (_, _) => InvalidateGoToLine();
        KindInput.SelectionChanged += (_, _) => InvalidateGoToLine();
        Closing += (_, _) => InvalidateGoToLine();
    }

    private static void BlockLineClipboard(object? sender, RoutedEventArgs args) => args.Handled = true;

    private void BeginGoToLine()
    {
        if (lineGesture) { return; }
        lineGesture = true;
        try
        {
            CancelGoToLine();
            var epoch = lineEpoch;
            var admitted = captureLineSource?.Invoke();
            bool Current() => IsVisible && Source.IsVisible && Source.IsEnabled && !Source.IsReadOnly
                && lineEpoch == epoch && ReferenceEquals(TopLevel.GetTopLevel(Source), this) && admitted?.Invoke() == true;
            if (!Current())
            { Report("Go to line unavailable. Explicitly Inspect/Create a supported owned source in the current eligible host; no source was read."); return; }
            OwnedDraftLineIndex index;
            try { index = new(Source.Text ?? string.Empty); }
            catch (InvalidDataException exception) { Report(exception.Message); return; }
            lineCurrent = Current;
            lineIndex = index;
            LineInput.Text = string.Empty;
            lineStatus.Text = $"Current displayed source: {index.Count} logical lines (one-based).";
            LineGo.IsEnabled = true;
            linePane.IsVisible = true;
            UpdateLayout();
            if (!Current() || !LineInput.Focus() || !LineInput.IsFocused || !Current())
            { InvalidateGoToLine(); Report("Go to line could not focus the current line input. Retry Go to line explicitly after recovery."); }
        }
        finally { lineGesture = false; }
    }

    private void GoToEnteredLine()
    {
        if (lineGesture) { return; }
        lineGesture = true;
        try
        {
            var current = lineCurrent;
            var index = lineIndex;
            if (current is null || index is null || !current())
            { InvalidateGoToLine(); Report("Go to line expired or unavailable. Retry Go to line explicitly; no navigation performed."); return; }
            if (!index.TryResolve(LineInput.Text ?? string.Empty, out var offset))
            { lineStatus.Text = $"Enter a canonical ASCII integer from 1 to {index.Count}; no spaces, signs or leading zeros."; return; }
            // Consume before native focus, which can synchronously reenter host/editor code.
            lineCurrent = null;
            lineIndex = null;
            LineGo.IsEnabled = false;
            Source.UpdateLayout();
            if (!Source.Focus() || !Source.IsFocused || !current())
            { Report("Go to line could not focus the same source. Retry explicitly after recovery; no navigation performed."); return; }
            var presenter = Source.GetVisualDescendants().OfType<TextPresenter>().SingleOrDefault();
            if (presenter is null || !current())
            { Report("Go to line native caret unavailable. Retry explicitly after recovery; no navigation performed."); return; }
            Source.CaretIndex = offset;
            if (!current() || !Source.IsFocused || Source.CaretIndex != offset)
            { Report("Go to line native caret acknowledgement failed. Retry explicitly after recovery; no success claimed."); return; }
            presenter.BringIntoView(presenter.TextLayout.HitTestTextPosition(offset));
            if (!current() || !Source.IsFocused || Source.CaretIndex != offset)
            { Report("Go to line expired during native navigation. Retry explicitly; no success claimed."); return; }
            CancelGoToLine();
            Report("Go to line completed in the current displayed source. No source edit, save or use.");
        }
        finally { lineGesture = false; }
    }

    internal void InvalidateGoToLine()
    {
        lineEpoch++;
        lineCurrent = null;
        lineIndex = null;
        LineInput.Text = string.Empty;
        LineGo.IsEnabled = false;
        lineStatus.Text = linePane.IsVisible ? "Request retired. Retry Go to line explicitly for the current source." : string.Empty;
    }

    private void CancelGoToLine()
    {
        linePane.IsVisible = false;
        InvalidateGoToLine();
    }
}
