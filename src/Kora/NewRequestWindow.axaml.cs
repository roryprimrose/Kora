using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Kora;

internal sealed partial class NewRequestWindow : Window
{
    internal NewRequestView View { get; }
#pragma warning disable VSTHRD003 // Compatibility adapter exposes only operations started by its owned view.
    internal Task Operation => View.Operation;
#pragma warning restore VSTHRD003
    internal Func<Task<ComposerDraftDecision>>? DecideDraft { get => View.DecideDraft; set => View.DecideDraft = value; }
    internal bool IsObservingDraftFind => View.IsObservingDraftFind;
    internal void ObserveDraftFindLifetime() => View.ObserveDraftFindLifetime();
    internal void BindDraftFind(Func<Func<bool>?> capture, System.ComponentModel.INotifyPropertyChanged host) => View.BindDraftFind(capture, host);
    internal Task BeginLiteralDictation() => View.BeginLiteralDictation();
    internal Task EndLiteralDictation() => View.EndLiteralDictation();
    internal void ClosePrivate() { Hide(); View.ClosePrivate(); }
    internal Task<bool> PrepareShutdown() => View.PrepareShutdown();
    internal void OpenPrivate() { View.OpenPrivate(); Show(); }
    internal T? FindControl<T>(string name) where T : Control => View.FindControl<T>(name);

    internal NewRequestWindow(NewRequestViewModel model, Func<Task> navigate, Action<Exception> navigationFailure,
        Func<Func<bool>>? captureDraftSource = null)
    {
        InitializeComponent();
        DataContext = model;
        View = new(model, this, navigate, navigationFailure, captureDraftSource);
        Content = View;
        View.CloseRequested += (_, _) => Close();
        Closed += (_, _) => View.Dispose();
        PropertyChanged += (_, args) => { if (args.Property == DataContextProperty) { View.DataContext = DataContext; } };
        AddHandler(KeyDownEvent, (_, args) =>
        {
            if (!ReferenceEquals(args.Source, this)) { return; }
            var forwarded = new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = args.Key, KeyModifiers = args.KeyModifiers };
            View.RaiseEvent(forwarded);
            args.Handled = forwarded.Handled;
        }, RoutingStrategies.Tunnel);
    }
}
