using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Kora;

public sealed partial class QuestionWindow : Window, INativeQuestionPresentation
{
    private readonly QuestionView view;
#pragma warning disable VSTHRD003 // Exposes only the activation operation owned by this adapter's native view.
    internal Task ActivationOperation => view.Operation;
#pragma warning restore VSTHRD003
    public QuestionWindow() => throw new InvalidOperationException("A trusted host question is required.");

    internal QuestionWindow(NativeQuestionViewModel state, NativeDetailRenderer renderer)
    {
        InitializeComponent();
        view = new(state, renderer, this);
        Content = view;
        view.Closed += (_, _) => Close();
        Closed += (_, _) => view.Dispose();
        AddHandler(KeyDownEvent, (_, args) =>
        {
            if (!ReferenceEquals(args.Source, this)) { return; }
            var forwarded = new KeyEventArgs
            { RoutedEvent = KeyDownEvent, Key = args.Key, KeyModifiers = args.KeyModifiers };
            view.RaiseEvent(forwarded);
            args.Handled = forwarded.Handled;
        }, RoutingStrategies.Tunnel);
    }

    internal T? FindControl<T>(string name) where T : Control => view.FindControl<T>(name);
    internal void ReportOutcome(string message) => view.ReportOutcome(message);
    internal Func<bool> CaptureFocusLifetime() => view.CaptureFocusLifetime();
    internal Task<bool> FocusExistingAsync(Func<bool> admitted) => view.FocusExistingAsync(admitted);
    bool INativeQuestionPresentation.IsPresented => view.IsPresented;
    void INativeQuestionPresentation.Activate() => view.Activate();
    void INativeQuestionPresentation.ReportOutcome(string message) => ReportOutcome(message);
    Func<bool> INativeQuestionPresentation.CaptureFocusLifetime() => CaptureFocusLifetime();
    Task<bool> INativeQuestionPresentation.FocusExistingAsync(Func<bool> admitted) => FocusExistingAsync(admitted);
}
