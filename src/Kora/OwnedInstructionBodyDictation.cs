using System.ComponentModel;

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

using Kora.Application.Voice;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora;

internal sealed class OwnedInstructionBodyDictation : INotifyPropertyChanged, IDisposable
{
    internal sealed record Binding(ComposerDictationTarget Target, HostRequest Request, Func<bool> Current,
        Func<CancellationToken, Task<bool>> Verify, Func<string, string> Proposed, string Label, CancellationToken Token);

    private readonly OwnedInstructionEditorWindow window;
    private readonly ComposerDictationService? service;
    private readonly Func<INotifyPropertyChanged, Binding?> capture;
    private readonly Action<Exception> failure;
    private readonly Button hold = new() { Content = "Hold to dictate body", Name = "DictateInstructionBody" };
    private readonly Button accept = new() { Content = "Accept literal body insertion", Name = "AcceptInstructionDictation", IsEnabled = false };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox original = ReviewText("Frozen instruction body");
    private readonly TextBox literal = ReviewText("Literal transcript, inert text only");
    private readonly TextBox proposed = ReviewText("Complete proposed source with unchanged metadata");
    private readonly Expander review = new() { Header = "Review literal body insertion", IsVisible = false };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private Binding? binding;
    private long epoch;
    private bool held;
    private bool ready;
    private bool applying;
    private bool disposed;
    private bool ownsRelease;
    private bool publicationSucceeded;
    private CancellationTokenSource? arming;
    private Task opening = Task.CompletedTask;

    public event PropertyChangedEventHandler? PropertyChanged;
    internal StackPanel Controls { get; } = new() { Spacing = 4 };
    internal Task Operation { get; private set; } = Task.CompletedTask;
    internal bool IsPending => binding is not null || !Operation.IsCompleted || !opening.IsCompleted
        || ownsRelease && service?.Quiescence.IsCompletedSuccessfully != true;
    private bool OwnsTurn => binding is { } current && service?.IsActiveFor(current.Target) == true;

    internal OwnedInstructionBodyDictation(OwnedInstructionEditorWindow window, ComposerDictationService? service,
        Func<INotifyPropertyChanged, Binding?> capture, Action<Exception> failure)
    {
        this.window = window;
        this.service = service;
        this.capture = capture;
        this.failure = failure;
        var buttons = new WrapPanel();
        buttons.Children.Add(hold);
        buttons.Children.Add(accept);
        var cancel = new Button { Content = "Cancel body dictation", Name = "CancelInstructionDictation" };
        buttons.Children.Add(cancel);
        Controls.Children.Add(buttons);
        Controls.Children.Add(status);
        var texts = new StackPanel { Spacing = 4 };
        texts.Children.Add(original); texts.Children.Add(literal); texts.Children.Add(proposed);
        review.Content = new ScrollViewer { Content = texts, MaxHeight = 220 };
        Controls.Children.Add(review);
        AutomationProperties.SetName(hold, "Hold Space, Enter or pointer to dictate literal text into this exact owned Instruction body caret or selection");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        hold.AddHandler(InputElement.PointerPressedEvent, (_, args) =>
        {
            if (!args.GetCurrentPoint(hold).Properties.IsLeftButtonPressed) { return; }
            args.Handled = true; args.Pointer.Capture(hold); Run(Begin);
        }, RoutingStrategies.Tunnel);
        hold.AddHandler(InputElement.PointerReleasedEvent, (_, args) =>
        { args.Handled = true; Run(End); args.Pointer.Capture(null); }, RoutingStrategies.Tunnel);
        hold.PointerCaptureLost += (_, _) => { if (held) { Cancel(); } };
        hold.AddHandler(InputElement.KeyDownEvent, (_, args) =>
        {
            if (args.Key is not (Key.Space or Key.Enter)) { return; }
            args.Handled = true; Run(Begin);
        }, RoutingStrategies.Tunnel);
        hold.AddHandler(InputElement.KeyUpEvent, (_, args) =>
        {
            if (args.Key is not (Key.Space or Key.Enter)) { return; }
            args.Handled = true; Run(End);
        }, RoutingStrategies.Tunnel);
        hold.LostFocus += (_, _) => { if (held) { Cancel(); } };
        accept.Click += (_, _) => Run(Accept);
        cancel.Click += (_, _) => Cancel();
        window.Deactivated += Deactivated;
        window.PropertyChanged += WindowChanged;
        window.Source.PropertyChanged += SourceChanged;
        window.AddHandler(InputElement.KeyDownEvent, Escape, RoutingStrategies.Tunnel);
        if (service is not null) { service.Changed += Changed; }
        timer.Tick += Tick;
        Render();
    }

    private static TextBox ReviewText(string label)
    {
        var text = new TextBox { IsReadOnly = true, AcceptsReturn = true, MinHeight = 70, IsUndoEnabled = false };
        AutomationProperties.SetName(text, label);
        return text;
    }

    private Task Begin()
    {
        if (held || disposed || !window.IsActive || !window.IsVisible || binding is not null
            || !opening.IsCompleted || service is null || service.IsActive || !service.Quiescence.IsCompletedSuccessfully)
        { return Task.CompletedTask; }
        return opening = Open();
    }

    private async Task Open()
    {
        held = true;
        var arm = Interlocked.Increment(ref epoch);
        var captured = capture(this) ?? throw new InvalidOperationException("No eligible owned body target.");
        binding = captured;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(captured.Token);
        arming = cancellation;
        ready = false;
        timer.Start();
        status.Text = captured.Label + " Checking the exact original before opening local capture.";
        using var activity = HostActivity.BeginRoot(captured.Request, HostActivityLayer.Desktop, HostOperation.Request);
        try
        {
            if (!await captured.Verify(cancellation.Token) || !Current(captured, arm))
            { Cancel(); activity.Complete(HostOperationOutcome.Cancelled); return; }
            ownsRelease = true;
            await service!.BeginAsync(captured.Target, () => !disposed && Interlocked.Read(ref epoch) == arm, captured.Token);
            activity.Complete(service.IsActiveFor(captured.Target) ? HostOperationOutcome.Completed : HostOperationOutcome.Cancelled);
            Render();
        }
        catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
        catch { activity.Complete(HostOperationOutcome.Failed); throw; }
        finally { if (ReferenceEquals(arming, cancellation)) { arming = null; } }
    }

    private bool Current(Binding captured, long arm) =>
        ReferenceEquals(binding, captured) && Interlocked.Read(ref epoch) == arm && captured.Current();

    private async Task End()
    {
        if (!held) { return; }
        held = false;
        if (!OwnsTurn) { await CancelAndRelease(); return; }
        var captured = binding!;
        var arm = Interlocked.Read(ref epoch);
        await service!.EndAsync();
        using var activity = HostActivity.BeginRoot(captured.Request, HostActivityLayer.Desktop, HostOperation.Presentation);
        try
        {
            if (!Current(captured, arm) || !OwnsTurn || !service.HasReview || !await captured.Verify(captured.Token))
            { Cancel(); activity.Complete(HostOperationOutcome.Cancelled); return; }
            _ = captured.Proposed(service.LiteralText);
            ready = true;
            activity.Complete(HostOperationOutcome.Completed);
            Render();
        }
        catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
        catch { activity.Complete(HostOperationOutcome.Failed); throw; }
    }

    private async Task Accept()
    {
        if (!ready || !OwnsTurn) { return; }
        var captured = binding!;
        var arm = Interlocked.Read(ref epoch);
        ready = false;
        Render();
        using var activity = HostActivity.BeginRoot(captured.Request, HostActivityLayer.Desktop, HostOperation.Presentation);
        try
        {
            publicationSucceeded = false;
            if (await captured.Verify(captured.Token) && Current(captured, arm) && OwnsTurn)
            { service!.Replace(); }
            else { Cancel(); }
            await CancelAndRelease();
            activity.Complete(publicationSucceeded ? HostOperationOutcome.Completed : HostOperationOutcome.Cancelled);
        }
        catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
        catch { activity.Complete(HostOperationOutcome.Failed); throw; }
    }

    internal bool Publish(Func<bool> publish)
    {
        applying = true;
        try { return publicationSucceeded = publish(); }
        finally { applying = false; }
    }

    private void Cancel()
    {
        held = false; ready = false;
        Interlocked.Increment(ref epoch);
        arming?.Cancel();
        if (OwnsTurn) { service!.Retire("Owned body dictation retired. Source and independent draft preserved; late results discarded."); }
        binding = null;
        timer.Stop();
        PropertyChanged?.Invoke(this, new(nameof(IsPending)));
        Render();
    }

    internal async Task CancelAndRelease()
    {
        Cancel();
#pragma warning disable VSTHRD003 // The shared capture owner retains the exact native release receipt.
        try { await opening; }
        catch (OperationCanceledException) { /* Cancellation still joins the exact pre-capture source read. */ }
        catch (Exception exception) when (exception is not OutOfMemoryException) { failure(exception); }
        if (ownsRelease && service is not null) { await service.Quiescence; ownsRelease = false; }
#pragma warning restore VSTHRD003
    }

    private void SourceChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (!applying && args.Property is { } property
            && (property == TextBox.TextProperty || property == TextBox.CaretIndexProperty
                || property == TextBox.SelectionStartProperty || property == TextBox.SelectionEndProperty
                || property == TextBox.IsReadOnlyProperty || property == InputElement.IsEnabledProperty))
        { Cancel(); }
    }
    private void WindowChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    { if (args.Property == Visual.IsVisibleProperty && !window.IsVisible) { Cancel(); } }
    private void Deactivated(object? sender, EventArgs args) => Cancel();
    private void Escape(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape || binding is null) { return; }
        args.Handled = true; Cancel();
    }
    private void Tick(object? sender, EventArgs args)
    {
        if (binding is { } current && !current.Current()) { Cancel(); }
        service?.Revalidate();
        if (binding is not null && opening.IsCompleted && !OwnsTurn) { Cancel(); }
    }
    private void Changed(object? sender, EventArgs args) => Render();
    private void Render()
    {
        var show = ready && OwnsTurn;
        accept.IsEnabled = show;
        review.IsVisible = show;
        review.IsExpanded = show;
        original.Text = show ? service!.OriginalBody : string.Empty;
        literal.Text = show ? service!.LiteralText : string.Empty;
        proposed.Text = show ? binding!.Proposed(service!.LiteralText) : string.Empty;
        hold.IsEnabled = !disposed && service is not null;
        status.Text = (binding?.Label ?? "Instruction body only; explicit local literal dictation. ") +
            (service?.Status ?? "Literal dictation unavailable. Microphone closed.");
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "Native pointer/key/button boundaries require void; failures retire capture and report through the editor.")]
    private async void Run(Func<Task> action)
    {
        try { await (Operation = action()); }
        catch (OperationCanceledException) { Cancel(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { Cancel(); failure(exception); }
    }

    public void Dispose()
    {
        disposed = true;
        Cancel();
        timer.Tick -= Tick;
        window.Deactivated -= Deactivated;
        window.PropertyChanged -= WindowChanged;
        window.Source.PropertyChanged -= SourceChanged;
        window.RemoveHandler(InputElement.KeyDownEvent, Escape);
        if (service is not null) { service.Changed -= Changed; }
    }
}
