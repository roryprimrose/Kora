using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using Kora.Application.ViewModels;

namespace Kora;

internal sealed partial class EvidenceWindow : Window
{
    internal EvidenceView View { get; }
    internal T? FindControl<T>(string name) where T : Control => View.FindControl<T>(name);
    internal Task<bool> PrepareTraceAsync(string traceId, Func<bool> admitted) => View.PrepareTraceAsync(traceId, admitted);

    internal EvidenceWindow(EvidenceViewModel model, MainViewModel? typographyHost = null)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = model;
        View = new(model, this, () => IsVisible && WindowState != WindowState.Minimized, typographyHost);
        Content = View;
        DataContextChanged += OnDataContextChanged;
        View.CloseRequested += (_, _) => Close();
        Closed += (_, _) =>
        {
            DataContextChanged -= OnDataContextChanged;
            View.Dispose();
        };
    }

    private void OnDataContextChanged(object? sender, EventArgs args) => View.DataContext = DataContext;
}
