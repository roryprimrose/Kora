using System.ComponentModel;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

using Kora.Application.Voice;

namespace Kora;

internal sealed class SessionDictationWindow : Window
{
    private readonly SessionsViewModel model;
    private readonly ComposerDictationService? service;
    private readonly Func<bool> sourceCurrent;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox original = new() { Name = "DictationOriginal", IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90 };
    private readonly TextBox literal = new() { Name = "DictationLiteral", IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90 };
    private readonly Button dictate = new() { Name = "DictateSession", Content = "Hold to dictate" };
    private readonly Button append = new() { Name = "AppendDictation", Content = "Append" };
    private readonly Button replace = new() { Name = "ReplaceDictation", Content = "Replace" };
    private ComposerDictationTarget? target;
    private bool held;
    private bool closing;
    private bool released;
    private long arm;
    internal Task Operation { get; private set; } = Task.CompletedTask;
    internal Task CloseOperation { get; private set; } = Task.CompletedTask;

    internal SessionDictationWindow(SessionsViewModel model)
    {
        this.model = model;
        service = model.Dictation;
        sourceCurrent = model.CaptureDictationPresentationEligibility();
        Title = "Dictate unsent session message";
        Width = 700;
        Height = 650;
        AutomationProperties.SetName(dictate, "Hold Space, Enter or the pointer to dictate into this exact session's unsent memory buffer");
        AutomationProperties.SetName(original, "Complete original unsent message, unchanged until explicit review");
        AutomationProperties.SetName(literal, "Complete literal transcription after confirmed microphone release");
        var cancel = new Button { Name = "CancelDictation", Content = "Cancel dictation" };
        var content = new StackPanel { Margin = new(20), Spacing = 12 };
        content.Children.Add(new TextBlock { Text = model.ComposerTarget, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock
        {
            Text = "Explicitly hold, then release. After the microphone is released, review Append / Replace / Cancel. Text stays in this session's memory only; return to the ordinary composer for a separate Submit.",
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(dictate);
        content.Children.Add(status);
        content.Children.Add(new TextBlock { Text = "Original unsent message" });
        content.Children.Add(original);
        content.Children.Add(new TextBlock { Text = "Literal transcription" });
        content.Children.Add(literal);
        var decisions = new WrapPanel();
        decisions.Children.Add(append);
        decisions.Children.Add(replace);
        decisions.Children.Add(cancel);
        content.Children.Add(decisions);
        Content = new ScrollViewer { Content = content };
        dictate.AddHandler(PointerPressedEvent, (_, args) =>
        {
            if (!args.GetCurrentPoint(dictate).Properties.IsLeftButtonPressed) { return; }
            args.Handled = true;
            args.Pointer.Capture(dictate);
            Run(BeginAsync);
        }, RoutingStrategies.Tunnel);
        dictate.AddHandler(PointerReleasedEvent, (_, args) =>
        {
            args.Handled = true;
            Run(EndAsync);
            args.Pointer.Capture(null);
        }, RoutingStrategies.Tunnel);
        dictate.PointerCaptureLost += (_, _) => { if (held) { Cancel(); } };
        dictate.AddHandler(KeyDownEvent, (_, args) =>
        {
            if (args.Key is not (Key.Space or Key.Enter)) { return; }
            args.Handled = true;
            Run(BeginAsync);
        }, RoutingStrategies.Tunnel);
        dictate.AddHandler(KeyUpEvent, (_, args) =>
        {
            if (args.Key is not (Key.Space or Key.Enter)) { return; }
            args.Handled = true;
            Run(EndAsync);
        }, RoutingStrategies.Tunnel);
        dictate.LostFocus += (_, _) => { if (held) { Cancel(); } };
        Deactivated += (_, _) => Cancel();
        PropertyChanged += (_, args) =>
        {
            if (args.Property == IsVisibleProperty && !IsVisible) { Cancel(); }
        };
        append.Click += (_, _) => { if (OwnsTurn) { service!.Append(); } };
        replace.Click += (_, _) => { if (OwnsTurn) { service!.Replace(); } };
        cancel.Click += (_, _) => Cancel();
        AddHandler(KeyDownEvent, (_, args) =>
        {
            if (args.Key != Key.Escape) { return; }
            args.Handled = true;
            Cancel();
        }, RoutingStrategies.Tunnel);
        model.PropertyChanged += SourceChanged;
        if (service is not null) { service.Changed += Changed; }
        Closing += (_, args) =>
        {
            if (released) { return; }
            args.Cancel = true;
            if (closing) { return; }
            closing = true;
            Hide();
            Cancel();
            CloseOperation = CloseAfterReleaseAsync();
        };
        Closed += (_, _) =>
        {
            model.PropertyChanged -= SourceChanged;
            if (service is not null) { service.Changed -= Changed; }
            original.Text = string.Empty;
            literal.Text = string.Empty;
        };
        Render();
    }

    private bool OwnsTurn => target is not null && service?.IsActiveFor(target) == true;

    internal Task BeginAsync()
    {
        if (held || closing || !IsVisible || !sourceCurrent() || !model.CanCompose || service is null
            || service.IsActive) { return Task.CompletedTask; }
        held = true;
        var generation = Interlocked.Increment(ref arm);
        target = model.CaptureDictationTarget();
        return service.BeginAsync(target, () => !closing && Interlocked.Read(ref arm) == generation
            && sourceCurrent(), CancellationToken.None);
    }

    internal Task EndAsync()
    {
        if (!held) { return Task.CompletedTask; }
        held = false;
        return OwnsTurn ? service!.EndAsync() : Task.CompletedTask;
    }

    private void Cancel()
    {
        held = false;
        Interlocked.Increment(ref arm);
        if (OwnsTurn) { service!.Retire("Session dictation cancelled. Unsent memory preserved; native release required."); }
        Render();
    }

    private void SourceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!sourceCurrent()) { Close(); }
    }

    private void Changed(object? sender, EventArgs args) => Render();

    private void Render()
    {
        var review = OwnsTurn && service!.HasReview && sourceCurrent();
        status.Text = service?.Status ?? "Literal dictation unavailable. Microphone closed.";
        original.Text = review ? service!.OriginalBody : string.Empty;
        literal.Text = review ? service!.LiteralText : string.Empty;
        append.IsEnabled = review;
        replace.IsEnabled = review;
        dictate.IsEnabled = !closing && sourceCurrent() && model.CanCompose && service is not null;
    }

    private async Task CloseAfterReleaseAsync()
    {
        try
        {
#pragma warning disable VSTHRD003 // Join the actual shared capture owner's release receipt before destroying this private native surface.
            if (service is not null) { await service.Quiescence; }
#pragma warning restore VSTHRD003
            released = true;
            Close();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { model.ReportComposerCloseFailure(exception); }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "Native pointer/key boundary; failures retire capture and use the session model's logged recovery status.")]
    private async void Run(Func<Task> operation)
    {
        try { await (Operation = operation()); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Cancel();
            model.ReportComposerCloseFailure(exception);
        }
    }
}
