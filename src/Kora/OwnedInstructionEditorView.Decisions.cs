using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace Kora;

internal sealed partial class OwnedInstructionEditorView
{
    private async Task<int> ChooseHosted(ResponseWindow primary, string title, string details, string? original, string? proposed,
        string[] choices, Func<Window, CancellationToken, Task<bool>>? spokenSave, CancellationToken token)
    {
        if (cancelDecision is not null) { throw new InvalidOperationException("Finish the current instruction decision first."); }
        var native = CapturePresentation();
        var reply = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var retired = false;
        using var reviewLifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var root = new DockPanel { Margin = new(12) };
        var explanation = new TextBlock { Text = details, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var disclosure = new ScrollViewer { Content = explanation, MaxHeight = 180 };
        DockPanel.SetDock(disclosure, Dock.Top);
        root.Children.Add(disclosure);
        var buttons = new WrapPanel { Margin = new(0, 10) };
        for (var index = 0; index < choices.Length; index++)
        {
            var answer = index;
            var button = new Button { Content = choices[index], Margin = new(3) };
            button.Click += (_, _) => reply.TrySetResult(native() ? answer : choices.Length - 1);
            buttons.Children.Add(button);
        }
        Exception? voiceFailure = null;
        Task voiceOperation = Task.CompletedTask;
        if (spokenSave is not null)
        {
            var voice = new Button { Content = "Reviewed complete source: exact save question", Margin = new(3) };
            voice.Click += async (_, _) =>
            {
                buttons.IsEnabled = false;
                voiceOperation = Answer();
                await voiceOperation;
            };
            async Task Answer()
            {
                try { reply.TrySetResult(await spokenSave(Owner, reviewLifetime.Token) && native() ? 0 : choices.Length - 1); }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                { voiceFailure = exception; reply.TrySetResult(choices.Length - 1); }
            }
            buttons.Children.Add(voice);
        }
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        var sources = new StackPanel { Spacing = 8 };
        var before = new TextBox { Text = original ?? "(missing/new file)", IsReadOnly = true, IsUndoEnabled = false,
            AcceptsReturn = true, Height = 220, FontFamily = new("Consolas") };
        var after = new TextBox { Text = proposed ?? "(delete)", IsReadOnly = true, IsUndoEnabled = false,
            AcceptsReturn = true, Height = 220, FontFamily = new("Consolas") };
        sources.Children.Add(new TextBlock { Text = "Complete original source" });
        sources.Children.Add(before);
        sources.Children.Add(new TextBlock { Text = "Complete proposed source" });
        sources.Children.Add(after);
        root.Children.Add(new ScrollViewer { Content = sources });
        void Cancel()
        {
            if (retired) { return; }
            reviewLifetime.Cancel();
            reply.TrySetResult(choices.Length - 1);
        }
        root.AddHandler(KeyDownEvent, (_, args) =>
        {
            if (args.Key != Key.Escape) { return; }
            args.Handled = true;
            Cancel();
        }, RoutingStrategies.Tunnel);
        cancelDecision = Cancel;
        using var registration = token.Register(() => Avalonia.Threading.Dispatcher.UIThread.Post(Cancel));
        try
        {
            primary.ShowNativeReview(root, title, root, native, shown => { if (!shown) { Cancel(); } });
            var result = await reply.Task;
            await reviewLifetime.CancelAsync();
#pragma warning disable VSTHRD003 // The review retains its exact spoken question until physical capture retirement.
            await voiceOperation;
#pragma warning restore VSTHRD003
            if (voiceFailure is not null && !token.IsCancellationRequested && native())
            { throw new InvalidOperationException("Exact save question failed: " + voiceFailure.Message + " Draft retained; recover through native Save.", voiceFailure); }
            return native() && !token.IsCancellationRequested ? result : choices.Length - 1;
        }
        finally
        {
            retired = true;
            cancelDecision = null;
            primary.RemoveNativeReview(root);
            before.Text = after.Text = explanation.Text = string.Empty;
            sources.Children.Clear();
            root.Children.Clear();
        }
    }
}
