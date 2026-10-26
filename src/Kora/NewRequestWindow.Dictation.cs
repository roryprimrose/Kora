using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Kora;

internal sealed partial class NewRequestWindow
{
    private void BindDictation()
    {
        DictateNewRequest.AddHandler(PointerPressedEvent, (_, args) =>
        {
            if (!args.GetCurrentPoint(DictateNewRequest).Properties.IsLeftButtonPressed) { return; }
            args.Handled = true;
            args.Pointer.Capture(DictateNewRequest);
            RunDictation(BeginLiteralDictation);
        }, RoutingStrategies.Tunnel);
        DictateNewRequest.AddHandler(PointerReleasedEvent, (_, args) =>
        {
            args.Handled = true;
            RunDictation(EndLiteralDictation);
            args.Pointer.Capture(null);
        }, RoutingStrategies.Tunnel);
        DictateNewRequest.PointerCaptureLost += (_, _) =>
        {
            if (dictationHeld) { CancelLiteralDictation(); }
        };
        DictateNewRequest.AddHandler(KeyDownEvent, (_, args) =>
        {
            if (args.Key is not (Key.Space or Key.Enter)) { return; }
            args.Handled = true;
            RunDictation(BeginLiteralDictation);
        }, RoutingStrategies.Tunnel);
        DictateNewRequest.AddHandler(KeyUpEvent, (_, args) =>
        {
            if (args.Key is not (Key.Space or Key.Enter)) { return; }
            args.Handled = true;
            RunDictation(EndLiteralDictation);
        }, RoutingStrategies.Tunnel);
        DictateNewRequest.LostFocus += (_, _) =>
        {
            if (dictationHeld) { CancelLiteralDictation(); }
        };
        Deactivated += (_, _) => CancelLiteralDictation();
        PropertyChanged += (_, args) =>
        {
            if (args.Property == Visual.IsVisibleProperty && !IsVisible) { CancelLiteralDictation(); }
        };
        CancelDictation.Click += (_, _) => RunDictation(CancelAndReleaseDictation);
        AppendDictation.Click += (_, _) => { if (model.OwnsDictation) { model.Dictation!.Append(); } };
        ReplaceDictation.Click += (_, _) => { if (model.OwnsDictation) { model.Dictation!.Replace(); } };
        AddHandler(KeyDownEvent, (_, args) =>
        {
            if (args.Key != Key.Escape || !model.OwnsDictation) { return; }
            args.Handled = true;
            RunDictation(CancelAndReleaseDictation);
        }, RoutingStrategies.Tunnel);
    }

    internal Task BeginLiteralDictation()
    {
        if (dictationHeld || privateClose || !IsVisible || model.Dictation is null || model.Dictation.IsActive) { return Task.CompletedTask; }
        dictationHeld = true;
        var arm = Interlocked.Increment(ref dictationArm);
        return model.Dictation.BeginAsync(model.Composer, () => !privateClose
            && Interlocked.Read(ref dictationArm) == arm, CancellationToken.None);
    }

    internal Task EndLiteralDictation()
    {
        if (!dictationHeld) { return Task.CompletedTask; }
        dictationHeld = false;
        return model.OwnsDictation ? model.Dictation!.EndAsync() : Task.CompletedTask;
    }

    private void CancelLiteralDictation()
    {
        dictationHeld = false;
        Interlocked.Increment(ref dictationArm);
        if (model.OwnsDictation)
        { model.Dictation!.Retire("Native dictation arm withdrawn. Original memory preserved; late results discarded."); }
    }

    private async Task CancelAndReleaseDictation()
    {
        CancelLiteralDictation();
#pragma warning disable VSTHRD003 // Native Cancel observes the exact capture owner's release receipt asynchronously.
        if (model.Dictation is { } dictation) { await dictation.Quiescence; }
#pragma warning restore VSTHRD003
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "Avalonia pointer/key callbacks require void; failures cancel dictation and invoke reportFailure.")]
    private async void RunDictation(Func<Task> operation)
    {
        try { await (Operation = operation()); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            CancelLiteralDictation();
            reportFailure(exception);
        }
    }
}
