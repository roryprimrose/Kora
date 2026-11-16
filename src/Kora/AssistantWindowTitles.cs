using Avalonia;
using Avalonia.Controls;

namespace Kora;

/// <summary>
/// Presents every desktop window title as "{assistant name} - {purpose}" without each window owning the rule.
/// The window's own title (literal or bound) is retained as its source and recomposed when either changes.
/// </summary>
internal sealed class AssistantWindowTitles : IDisposable
{
    private static readonly AttachedProperty<string?> SourceTitleProperty =
        AvaloniaProperty.RegisterAttached<AssistantWindowTitles, Window, string?>("AssistantSourceTitle");

    private readonly Func<IEnumerable<Window>> openWindows;
    private readonly AssistantNameDisplay display;
    private readonly IDisposable titleChanges;
    private readonly IDisposable windowOpens;
    private bool applying;
    private bool disposed;

    private AssistantWindowTitles(Func<IEnumerable<Window>> openWindows, AssistantNameDisplay display)
    {
        this.openWindows = openWindows;
        this.display = display;
        titleChanges = Window.TitleProperty.Changed.AddClassHandler<Window>((window, _) => OnTitleChanged(window));
        windowOpens = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => Apply(window));
        display.PropertyChanged += OnNameChanged;
    }

    internal static AssistantWindowTitles Install(Func<IEnumerable<Window>> openWindows, AssistantNameDisplay display)
    {
        var titles = new AssistantWindowTitles(openWindows, display);
        titles.ApplyAll();
        return titles;
    }

    internal static string Compose(string? source, string assistantName)
    {
        if (string.IsNullOrWhiteSpace(source)) { return assistantName; }
        var trimmed = source.Trim();
        if (string.Equals(trimmed, assistantName, StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith(assistantName + " ", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }
        return $"{assistantName} - {trimmed}";
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        display.PropertyChanged -= OnNameChanged;
        titleChanges.Dispose();
        windowOpens.Dispose();
    }

    private void OnNameChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => ApplyAll();

    private void OnTitleChanged(Window window)
    {
        if (applying || disposed) { return; }
        window.SetValue(SourceTitleProperty, window.Title);
        Apply(window);
    }

    private void ApplyAll()
    {
        foreach (var window in openWindows().ToArray()) { Apply(window); }
    }

    private void Apply(Window window)
    {
        if (disposed) { return; }
        var source = window.GetValue(SourceTitleProperty) ?? window.Title;
        window.SetValue(SourceTitleProperty, source);
        var composed = Compose(source, display.Name);
        if (string.Equals(window.Title, composed, StringComparison.Ordinal)) { return; }
        applying = true;
        try { window.SetCurrentValue(Window.TitleProperty, composed); }
        finally { applying = false; }
    }
}
