using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Kora.Application.Skills;
using Kora.Core.Skills;

namespace Kora;

internal sealed class ObservationWorkflowWindow : Window, IOwnedSkillSaveReview
{
    internal readonly TextBox IdInput = new();
    internal readonly TextBox VersionInput = new() { Text = "1.0.0" };
    internal readonly TextBox NameInput = new();
    internal readonly TextBox DescriptionInput = new();
    internal readonly TextBox Definition = new() { AcceptsReturn = true, MinHeight = 120, TextWrapping = TextWrapping.Wrap };
    internal readonly ComboBox Saved = new();
    internal readonly ComboBox Targets = new();
    internal readonly TextBox Request = new() { AcceptsReturn = true, MinHeight = 60, TextWrapping = TextWrapping.Wrap };
    internal readonly TextBox ReviewText = new() { IsReadOnly = true, AcceptsReturn = true, MinHeight = 120, TextWrapping = TextWrapping.Wrap };
    internal readonly TextBox Result = new() { IsReadOnly = true, AcceptsReturn = true, MinHeight = 120, TextWrapping = TextWrapping.Wrap };
    internal readonly TextBlock Status = new() { TextWrapping = TextWrapping.Wrap };
    internal event Action? DraftChanged;
    internal event Action? InvocationChanged;
    private readonly Func<bool> eligible;
    private Window? dialog;
    private TaskCompletionSource<bool>? pending;
    private Func<bool>? pendingCurrent;

    internal ObservationWorkflowWindow(Func<string, Task> run, Func<bool> eligible)
    {
        this.eligible = eligible;
        Title = "Kora-owned observation workflow";
        Width = 900;
        Height = 800;
        var panel = new StackPanel { Margin = new(12), Spacing = 6 };
        panel.Children.Add(new TextBlock
        {
            Text = "Native-only, one to six explicitly ordered original host observations. No model, scripts, network, probes or settings writes. Save remains disabled; separately review source, original request and Active target, Enable, then Invoke. Each step needs independent approval. Results remain in this private window, not conversation history.",
            TextWrapping = TextWrapping.Wrap,
        });
        foreach (var field in new[] { ("Skill ID", IdInput), ("New version", VersionInput), ("Name", NameInput),
            ("Description", DescriptionInput), ("Complete JSON workflow declaration", Definition) })
        {
            Add(field.Item1, field.Item2);
            field.Item2.PropertyChanged += (_, args) => { if (args.Property == TextBox.TextProperty) { DraftChanged?.Invoke(); } };
        }
        Buttons("Create", "Validate", "Save", "List", "Reopen");
        Add("Saved disabled versions", Saved);
        Buttons("Targets", "Next targets");
        Add("Exact Active target", Targets);
        Add("Unchanged original request", Request);
        Request.PropertyChanged += (_, args) => { if (args.Property == TextBox.TextProperty) { InvocationChanged?.Invoke(); } };
        Targets.SelectionChanged += (_, _) => InvocationChanged?.Invoke();
        Saved.SelectionChanged += (_, _) => InvocationChanged?.Invoke();
        Buttons("Review", "Enable", "Invoke", "Status", "Cancel", "Close");
        Add("Exact source, request, target and execution bounds", ReviewText);
        Add("Complete source-labelled result (no partial success)", Result);
        panel.Children.Add(Status);
        Content = new ScrollViewer { Content = panel };

        void Add(string name, Control control)
        {
            panel.Children.Add(new TextBlock { Text = name });
            AutomationProperties.SetName(control, name);
            panel.Children.Add(control);
        }
        void Buttons(params string[] actions)
        {
            var row = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var action in actions)
            {
                var button = new Button { Content = action, Margin = new(3) };
                button.Click += async (_, _) => await run(action.ToLowerInvariant().Replace(' ', '-'));
                row.Children.Add(button);
            }
            panel.Children.Add(row);
        }
    }

    internal OwnedDeclarativeSkillDocument Document() => OwnedDeclarativeSkillDocument.CreateObservationWorkflow(
        IdInput.Text ?? "", VersionInput.Text ?? "", NameInput.Text ?? "", DescriptionInput.Text ?? "", Definition.Text ?? "");

    public Task<bool> Review(OwnedDeclarativeSkillDocument document, string destination, CancellationToken token) =>
        Confirm("Review exact workflow package", destination + "\nSave exactly manifest.yaml and instructions.md, disabled. No actions run.\n"
            + document.ManifestText + "\n" + document.Guidance, "Continue to separate Save confirmation", eligible, token);

    public Task<bool> ConfirmPublication(OwnedDeclarativeSkillDocument document, string destination, CancellationToken token) =>
        Confirm("Save disabled exact workflow", destination + "\nPublish this exact new immutable revision, disabled?\n"
            + document.ManifestText + "\n" + document.Guidance, "Save disabled revision", eligible, token);

    internal async Task<bool> Confirm(string title, string text, string confirm, Func<bool> current, CancellationToken token)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        { return await Dispatcher.UIThread.InvokeAsync(() => Confirm(title, text, confirm, current, token)); }
        token.ThrowIfCancellationRequested();
        if (!IsVisible || !current()) { throw new InvalidOperationException("The private exact review is no longer current."); }
        if (pending is not null) { throw new InvalidOperationException("Another exact native review is pending."); }
        var returnFocus = FocusManager?.GetFocusedElement() as Control;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending = completion;
        pendingCurrent = () => IsVisible && current();
        var opened = new Window { Title = title, Width = 780, Height = 650 };
        dialog = opened;
        var content = new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, TabIndex = 0 };
        AutomationProperties.SetName(content, "Complete exact review");
        var yes = new Button { Content = confirm, TabIndex = 1 };
        var no = new Button { Content = "Cancel", TabIndex = 2 };
        yes.Click += (_, _) => CompleteApproval(true);
        no.Click += (_, _) => CompleteApproval(false);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { yes, no } };
        var panel = new DockPanel { Margin = new(12) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        panel.Children.Add(content);
        opened.Content = panel;
        var closed = false;
        opened.Closed += (_, _) => { closed = true; completion.TrySetResult(false); };
        opened.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) { args.Handled = true; CompleteApproval(false); }
        };
        PropertyChanged += OwnerChanged;
        using var registration = token.Register(() => Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(dialog, opened)) { return; }
            completion.TrySetCanceled(token);
            pendingCurrent = null;
            opened.Content = null;
            CloseReview();
        }));
        try
        {
            var nativeClosure = opened.ShowDialog(this);
            // Avalonia can finish making a window visible after a reentrant close in Opened.
            if (closed) { opened.Hide(); }
            if (!IsVisible || !current() || token.IsCancellationRequested)
            {
                if (token.IsCancellationRequested) { completion.TrySetCanceled(token); }
                else { completion.TrySetResult(false); }
                opened.Content = null;
                CloseReview();
            }
            else if (!completion.Task.IsCompleted) { opened.UpdateLayout(); content.Focus(); }
#pragma warning disable VSTHRD003 // Hold the exact native modal lifetime, not merely the selected choice.
            await nativeClosure;
            var approved = await completion.Task;
#pragma warning restore VSTHRD003
            return approved && pendingCurrent?.Invoke() == true && !token.IsCancellationRequested;
        }
        finally
        {
            PropertyChanged -= OwnerChanged;
            content.Text = "";
            opened.Content = null;
            opened.Close();
            if (ReferenceEquals(dialog, opened)) { pending = null; pendingCurrent = null; dialog = null; }
            if (IsVisible && current() && !token.IsCancellationRequested
                && returnFocus is not null && ReferenceEquals(TopLevel.GetTopLevel(returnFocus), this)
                && returnFocus.IsEffectivelyEnabled && returnFocus.IsEffectivelyVisible)
            { returnFocus.Focus(); }
        }

        void OwnerChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == IsVisibleProperty && !IsVisible)
            {
                completion.TrySetResult(false);
                pendingCurrent = null;
                opened.Content = null;
                CloseReview();
            }
        }
    }

    internal void CompleteApproval(bool approved)
    {
        if (pending is null) { throw new InvalidOperationException("No exact native review is pending."); }
        pending.TrySetResult(approved && pendingCurrent?.Invoke() == true);
        if (!approved) { pendingCurrent = null; }
        CloseReview();
    }

    internal void ClearReview()
    {
        pending?.TrySetResult(false);
        pendingCurrent = null;
        if (dialog is { } opened) { opened.Content = null; }
        CloseReview();
        ReviewText.Text = "";
        Result.Text = "";
    }

    private void CloseReview()
    {
        if (dialog is not { } opened) { return; }
        // Queue closure so retirement during native Show initialization cannot orphan the modal.
        Dispatcher.UIThread.Post(() => { if (ReferenceEquals(dialog, opened)) { opened.Close(); } });
    }
}
