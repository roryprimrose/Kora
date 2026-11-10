using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Kora;

internal sealed partial class AllWorkWindow : Window
{
    private readonly AllWorkView view;
    internal Func<bool>? FocusFindMatch { get => view.FocusFindMatch; set => view.FocusFindMatch = value; }
    internal Func<bool>? FocusFindQuery { get => view.FocusFindQuery; set => view.FocusFindQuery = value; }
    internal T? FindControl<T>(string name) where T : Control => view.FindControl<T>(name);

    internal AllWorkWindow(AllWorkViewModel model)
    {
        InitializeComponent();
        view = new(model, this);
        Content = view;
        DataContext = model;
        view.CloseRequested += (_, _) => Close();
        Closed += (_, _) => view.Dispose();
        PropertyChanged += (_, args) =>
        {
            if (args.Property == DataContextProperty) { view.DataContext = DataContext; }
        };
        AddHandler(KeyDownEvent, (_, args) =>
        {
            if (!ReferenceEquals(args.Source, this)) { return; }
            var forwarded = new KeyEventArgs
            { RoutedEvent = KeyDownEvent, Key = args.Key, KeyModifiers = args.KeyModifiers };
            view.RaiseEvent(forwarded);
            args.Handled = forwarded.Handled;
        }, RoutingStrategies.Tunnel);
    }
}
