using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Styling;

namespace Kora.CodeHighlighting;

internal sealed class NativeCodeBlock : StackPanel
{
    private readonly CodeHighlightSession session = new();
    private readonly TextBlock reader;
    private readonly TextBlock status;
    private readonly CheckBox plain;
    private readonly CodeSource source;
    private CodeProjection? projection;
    private bool attached;
    private bool retired;

    internal NativeCodeBlock(CodeSource source, TextBlock reader, bool highlightingAvailable = true)
    {
        this.source = source;
        this.reader = reader;
        reader.Text = source.Text;
        AutomationProperties.SetName(reader, source.Text);
        status = new TextBlock { Text = $"{source.Language}: plain source (highlighting pending)." };
        plain = new CheckBox { Content = "Plain code", IsChecked = false };
        AutomationProperties.SetName(plain, "Show code without syntax colors");
        Children.Add(status);
        Children.Add(plain);
        Children.Add(reader);
        plain.IsCheckedChanged += (_, _) => Project();
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

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (retired) { return; }
        attached = true;
        if (projection is null) { Pending = RenderAsync(); }
        else { Project(); }
    }

    private async Task RenderAsync()
    {
        projection = await session.RenderAsync(source);
        if (!attached || retired || projection is null) { return; }
        Project();
    }

    internal void Retire()
    {
        attached = false;
        retired = true;
        session.Retire();
        projection = null;
        reader.Inlines?.Clear();
        reader.Text = string.Empty;
        AutomationProperties.SetName(reader, string.Empty);
        status.Text = "Code view retired.";
    }

    internal Task JoinAsync() => session.JoinAsync();

    private void Project()
    {
        if (retired) { return; }
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
