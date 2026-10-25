using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;

using Kora.Application.Presentation;
using Kora.Application.ViewModels;

namespace Kora;

public sealed partial class DetailWindow : Window, IDetailView
{
    internal DetailContentView Reader { get; }
    internal T? FindControl<T>(string name) where T : Control => Reader.FindControl<T>(name);

    public DetailWindow() => throw new InvalidOperationException("Use the explicit host-owned native detail route.");

    internal DetailWindow(DetailViewerState state, NativeDocumentResult result, Func<bool, int?, int, Task> copySource,
        MainViewModel? typographyHost = null)
    {
        AvaloniaXamlLoader.Load(this);
        Title = state.Content?.Title + " — details";
        Reader = new(state, result, copySource, typographyHost);
        Content = Reader;
        Reader.CloseRequested += (_, _) => Close();
        AddHandler(KeyDownEvent, (sender, args) =>
        {
            if (ReferenceEquals(args.Source, this)) { Reader.OnKeyDown(sender, args); }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        Opened += (_, _) => Reader.FocusReader();
        Closed += (_, _) => Reader.ClearPresentation();
    }

    void IDetailView.ShowOwned(Window? owner)
    {
        if (owner is null) { throw new InvalidOperationException("A native detail viewer requires a host window owner."); }
        Show(owner);
    }

    public void ClearAndClose()
    {
        Reader.ClearPresentation();
        Close();
    }
}
