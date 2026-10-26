using System.Diagnostics.CodeAnalysis;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;

using Kora.Controls;

namespace Kora.Diagrams;

internal sealed class MermaidDetailView : StackPanel, IAsyncDisposable
{
    private readonly MermaidRenderSession session;
    private Func<bool> admitted;
    private readonly ComboBox diagrams;
    private readonly Button render = new() { Content = "Render selected Mermaid diagram" };
    private readonly Button cancel = new() { Content = "Cancel diagram", IsEnabled = false };
    private readonly NamedTextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel preview = new();
    private IReadOnlyList<MermaidSource> sources;
    private bool retired;

    internal MermaidDetailView(IReadOnlyList<MermaidSource> sources, Func<bool> admitted, IMermaidRenderer renderer)
    {
        this.sources = sources;
        this.admitted = admitted;
        session = new(renderer);
        Spacing = 6;
        diagrams = new() { ItemsSource = Enumerable.Range(1, sources.Count).Select(number => $"Diagram {number}").ToArray(), SelectedIndex = 0 };
        AutomationProperties.SetName(diagrams, "Complete bounded Mermaid block; selection does not render");
        AutomationProperties.SetName(status, "Mermaid renderer status; source remains authoritative");
        status.Text = $"{MermaidProfile.Name}: explicit render only. Unknown native qualification remains unavailable. Exact document source is unchanged below.";
        render.Click += OnRenderRequested;
        cancel.Click += (_, _) =>
        {
            session.Cancel();
            preview.Children.Clear();
            status.Text = "Diagram cancellation requested; worker ownership retained until joined. Exact source remains available.";
        };
        diagrams.SelectionChanged += (_, _) =>
        {
            session.Cancel();
            preview.Children.Clear();
        };
        Children.Add(new TextBlock { Text = "Passive Mermaid diagram / exact source", FontWeight = FontWeight.SemiBold });
        Children.Add(diagrams);
        Children.Add(new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children = { render, cancel } });
        Children.Add(status);
        Children.Add(new ScrollViewer { MaxHeight = 320, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = preview });
    }

    [SuppressMessage("Usage", "VSTHRD003", Justification = "Observes the owned worker's join without blocking the dispatcher.")]
    internal Task WhenQuiescent => session.WhenQuiescent;

    [SuppressMessage("Usage", "VSTHRD100", Justification = "Avalonia Click boundary; expected rendering failures are shown explicitly.")]
    private async void OnRenderRequested(object? sender, RoutedEventArgs eventArgs)
    {
        try { await RenderSelectedAsync(); }
        catch (InvalidOperationException)
        {
            if (!retired) { status.Text = "Diagram rendering failed; exact source remains available. No diagram success is claimed."; }
        }
    }

    internal async Task RenderSelectedAsync()
    {
        if (retired || !admitted()) { Retire(); return; }
        var selected = diagrams.SelectedIndex;
        if (selected < 0 || selected >= sources.Count || session.IsRunning) { return; }
        var source = sources[selected];
        preview.Children.Clear();
        render.IsEnabled = false;
        cancel.IsEnabled = true;
        diagrams.IsEnabled = false;
        status.Text = "Rendering bounded native Mermaid; no browser, scripts, network, plugins or host bridge.";
        try
        {
            var result = await session.StartAsync(source.Text, () => !retired && admitted());
            if (retired || !admitted()) { Retire(); return; }
            if (result is null)
            {
                status.Text = "Diagram cancelled and worker joined; no late diagram published. Exact source remains available.";
                return;
            }
            status.Text = result.Failure == MermaidFailure.None
                ? $"Rendered {MermaidProfile.Name} passive native subset. Exact diagram source follows; document Find/copy remain unchanged."
                : $"{MermaidProfile.Name}: {result.Failure}; exact source fallback. Nothing was truncated or enabled.";
            var native = result.Drawing is { } drawing ? MermaidNativeDrawing.Create(drawing) : null;
            if (retired || !admitted()) { Retire(); return; }
            if (native is not null) { preview.Children.Add(native); }
            var exact = new NamedTextBlock { Text = source.Text, Focusable = true, TextWrapping = TextWrapping.Wrap };
            exact.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("KoraForegroundBrush"));
            AutomationProperties.SetName(exact, "Exact immutable Mermaid source; use native Details copy policy");
            preview.Children.Add(exact);
        }
        finally
        {
            if (!retired)
            {
                render.IsEnabled = true;
                cancel.IsEnabled = false;
                diagrams.IsEnabled = true;
            }
        }
    }

    internal void Retire()
    {
        if (retired) { return; }
        retired = true;
        session.Retire();
        admitted = () => false;
        sources = [];
        diagrams.ItemsSource = null;
        render.Click -= OnRenderRequested;
        preview.Children.Clear();
        status.Text = string.Empty;
        Children.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        Retire();
        await session.DisposeAsync();
    }
}
