using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace Kora;

internal sealed partial class SessionMetricsPanel : UserControl
{
    internal Task Operation { get; private set; } = Task.CompletedTask;

    public SessionMetricsPanel()
    {
        AvaloniaXamlLoader.Load(this);
        this.FindControl<Button>("RefreshSessionMetrics")!.Click += async (_, _) => await RefreshAsync();
        var expander = this.FindControl<Expander>("MetricsExpander")!;
        expander.PropertyChanged += async (_, args) =>
        {
            if (args.Property == Expander.IsExpandedProperty && expander.IsExpanded) { await RefreshAsync(); }
        };
    }

    private async Task RefreshAsync()
    {
        if (DataContext is not SessionsViewModel model || !model.CanReadMetrics
            || TopLevel.GetTopLevel(this) is not Window) { return; }
        using var presenter = new ReplyPresentationFence(this);
        await (Operation = model.ReadMetricsAsync(presenter.Capture()));
    }
}
