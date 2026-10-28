using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Kora.CodeHighlighting;

internal sealed class NativeCodeBlock : StackPanel
{
    private readonly CodeHighlightSession session = new();
    private readonly TextBlock reader;
    private readonly TextBlock status;
    private readonly CheckBox plain;
    private CodeSource? source;
    private readonly Func<bool>? canRead;
    private readonly CheckBox? select;
    private readonly CheckBox? wrap;
    private readonly ScrollViewer? viewport;
    private readonly TextBox? selectionReader;
    private CodeProjection? projection;
    private bool attached;
    private bool retired;

    internal NativeCodeBlock(CodeSource source, TextBlock reader, bool highlightingAvailable = true, Func<bool>? canRead = null)
    {
        this.source = source;
        this.reader = reader;
        this.canRead = canRead;
        reader.Text = source.Text;
        AutomationProperties.SetName(reader, source.Text);
        status = new TextBlock { Text = $"{source.Language}: plain source (highlighting pending)." };
        plain = new CheckBox { Content = "Plain code", IsChecked = false };
        AutomationProperties.SetName(plain, "Show code without syntax colors");
        Children.Add(status);
        Children.Add(plain);
        if (canRead is null) { Children.Add(reader); }
        else
        {
            select = new CheckBox { Content = "Select literal code locally (no copy)" };
            wrap = new CheckBox { Content = "Wrap code", IsChecked = true };
            selectionReader = new TextBox
            {
                Text = source.Text, IsReadOnly = true, AcceptsReturn = true, IsUndoEnabled = false,
                TextWrapping = TextWrapping.Wrap, FontFamily = reader.FontFamily, FontSize = reader.FontSize,
                ContextMenu = null, ContextFlyout = null, IsVisible = false,
            };
            AutomationProperties.SetName(selectionReader, "Immutable literal code; local visual selection only, clipboard unavailable");
            ScrollViewer.SetHorizontalScrollBarVisibility(selectionReader, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(selectionReader, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
            selectionReader.CopyingToClipboard += BlockClipboard;
            selectionReader.CuttingToClipboard += BlockClipboard;
            selectionReader.PastingFromClipboard += BlockClipboard;
            selectionReader.PropertyChanged += (_, args) =>
            {
                if (!retired && (args.Property == TextBox.SelectionStartProperty || args.Property == TextBox.SelectionEndProperty
                    || args.Property == TextBox.CaretIndexProperty)) { _ = AdmitReading(); }
            };
            selectionReader.AddHandler(ContextRequestedEvent, BlockContext, RoutingStrategies.Tunnel);
            var reading = new Grid { Children = { reader, selectionReader } };
            viewport = new ScrollViewer
            {
                Content = reading, MaxHeight = 360,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            };
            Children.Add(select);
            Children.Add(wrap);
            Children.Add(viewport);
            select.IsCheckedChanged += (_, _) =>
            {
                if (!AdmitReading()) { return; }
                selectionReader.IsVisible = select.IsChecked == true;
                reader.IsVisible = !selectionReader.IsVisible;
                if (selectionReader.IsVisible) { selectionReader.Focus(); }
            };
            wrap.IsCheckedChanged += (_, _) =>
            {
                if (!AdmitReading()) { return; }
                reader.TextWrapping = selectionReader.TextWrapping = wrap.IsChecked == true ? TextWrapping.Wrap : TextWrapping.NoWrap;
                viewport.HorizontalScrollBarVisibility = wrap.IsChecked == true
                    ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
            };
            AddHandler(KeyDownEvent, OnReadingKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
            AddHandler(PointerPressedEvent, (_, args) =>
            {
                if (!AdmitReading()) { args.Handled = true; }
            }, RoutingStrategies.Tunnel, handledEventsToo: true);
            AddHandler(PointerWheelChangedEvent, (_, args) =>
            {
                if (!AdmitReading()) { args.Handled = true; }
            }, RoutingStrategies.Tunnel, handledEventsToo: true);
            GotFocus += (_, _) => { _ = AdmitReading(); };
        }
        plain.IsCheckedChanged += (_, _) => { if (AdmitReading()) { Project(); } };
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += (_, _) => Retire();
        PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.Property.Name, "ActualThemeVariant", StringComparison.Ordinal)) { Project(); }
        };
        if (!highlightingAvailable)
        {
            projection = new(source, CodeHighlightOutcome.ViewLimit, []);
            Project();
        }
    }

    internal TextBlock Reader => reader;
    internal string Status => status.Text ?? string.Empty;
    internal CheckBox PlainToggle => plain;
    internal Task Pending { get; private set; } = Task.CompletedTask;
    internal TextBox? SelectionReader => selectionReader;
    internal CheckBox? SelectToggle => select;
    internal CheckBox? WrapToggle => wrap;
    internal ScrollViewer? ReadingViewport => viewport;

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (retired) { return; }
        attached = true;
        if (projection is null) { Pending = RenderAsync(); }
        else { Project(); }
    }

    private async Task RenderAsync()
    {
        var admittedSource = source;
        if (admittedSource is null) { return; }
        var result = await session.RenderAsync(admittedSource);
        if (!attached || retired || result is null) { return; }
        projection = result;
        Project();
    }

    internal void Retire()
    {
        attached = false;
        retired = true;
        session.Retire();
        projection = null;
        source = null;
        reader.Inlines?.Clear();
        reader.Text = string.Empty;
        AutomationProperties.SetName(reader, string.Empty);
        if (selectionReader is not null)
        {
            selectionReader.SelectionStart = selectionReader.SelectionEnd = selectionReader.CaretIndex = 0;
            selectionReader.Text = string.Empty;
            selectionReader.IsEnabled = false;
            AutomationProperties.SetName(selectionReader, string.Empty);
            select!.IsEnabled = wrap!.IsEnabled = false;
        }
        status.Text = "Code view retired.";
    }

    internal Task JoinAsync() => session.JoinAsync();

    private void Project()
    {
        if (!AdmitReading() || this.source is not { } source) { return; }
        reader.Inlines?.Clear();
        reader.Text = source.Text;
        if (projection is null) { return; }
        status.Text = $"{source.Language}: {projection.Outcome}"
            + (projection.Outcome == CodeHighlightOutcome.Highlighted && plain.IsChecked != true
                ? " (offline-code-v1)." : "; exact plain code.");
        if (plain.IsChecked == true || projection.Outcome != CodeHighlightOutcome.Highlighted) { return; }
        reader.Text = null;
        var position = 0;
        foreach (var token in projection.Tokens)
        {
            if (token.Start > position) { reader.Inlines!.Add(new Run(source.Text[position..token.Start])); }
            reader.Inlines!.Add(new Run(source.Text.Substring(token.Start, token.Length))
            {
                Foreground = Brush(token.Kind, ActualThemeVariant == ThemeVariant.Dark),
                FontWeight = token.Kind == CodeTokenKind.Keyword ? FontWeight.SemiBold : FontWeight.Normal,
            });
            position = token.Start + token.Length;
        }

        if (position < source.Text.Length) { reader.Inlines!.Add(new Run(source.Text[position..])); }
    }

    private bool AdmitReading()
    {
        if (retired) { return false; }
        if (canRead is null || canRead()) { return true; }
        Retire();
        return false;
    }

    private void OnReadingKeyDown(object? sender, KeyEventArgs args)
    {
        if (!AdmitReading()) { args.Handled = true; return; }
        if ((args.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != KeyModifiers.None
            && args.Key is Key.C or Key.X or Key.V or Key.Insert
            || args.KeyModifiers.HasFlag(KeyModifiers.Shift) && args.Key is Key.Insert or Key.Delete)
        {
            args.Handled = true;
            status.Text = "Code selection is local visual reading only; clipboard copy, cut and paste are unavailable.";
        }
    }

    private void BlockClipboard(object? sender, RoutedEventArgs args)
    {
        args.Handled = true;
        if (AdmitReading()) { status.Text = "Code selection is local visual reading only; clipboard copy, cut and paste are unavailable."; }
    }

    private void BlockContext(object? sender, ContextRequestedEventArgs args)
    {
        args.Handled = true;
        _ = AdmitReading();
    }

    internal static IBrush Brush(CodeTokenKind kind, bool dark) => new SolidColorBrush(Color.Parse((kind, dark) switch
    {
        (CodeTokenKind.Keyword, true) => "#8FC7FF",
        (CodeTokenKind.Keyword, false) => "#004578",
        (CodeTokenKind.String, true) => "#F2BFAA",
        (CodeTokenKind.String, false) => "#8A2500",
        (CodeTokenKind.Comment, true) => "#A8CCA0",
        (CodeTokenKind.Comment, false) => "#315B28",
        (CodeTokenKind.Number, true) => "#D7BFFF",
        (CodeTokenKind.Number, false) => "#623292",
        (CodeTokenKind.Added, true) => "#A8CCA0",
        (CodeTokenKind.Added, false) => "#315B28",
        (CodeTokenKind.Removed, true) => "#FFB4B4",
        (CodeTokenKind.Removed, false) => "#9C2020",
        (_, true) => "#E8E8E8",
        (_, false) => "#242424",
    }));
}
