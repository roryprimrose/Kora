using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Kora.Application.Skills;
using Kora.Core.Skills;

namespace Kora;

internal sealed class ObservationWorkflowView : UserControl, IOwnedSkillSaveReview, IDisposable
{
    internal Window Owner { get; }
    private readonly NativeViewLifetime presentation;
    private bool disposed;
    internal bool IsNativePresented => presentation.IsPresented;
    internal Func<bool> CapturePresentation() => presentation.Capture();
    internal event EventHandler<WindowClosingEventArgs>? Closing;
    internal event EventHandler? PresentationLost;
    internal event Action? PresentRequested;
    internal void Activate() => Owner.Activate();
    internal void Hide() => IsVisible = false;
    internal void Close() => Dispose();
    internal void Show()
    {
        IsVisible = true;
        if (PresentRequested is { } present) { present(); }
        else { Owner.Show(); }
        presentation.Refresh();
    }
    internal readonly TextBox IdInput = new();
    internal readonly TextBox VersionInput = new() { Text = "1.0.0" };
    internal readonly TextBox NameInput = new();
    internal readonly TextBox DescriptionInput = new();
    internal readonly TextBox Definition = new() { AcceptsReturn = true, MinHeight = 120, MaxHeight = 300, TextWrapping = TextWrapping.Wrap };
    internal readonly ComboBox Saved = new();
    internal readonly ComboBox Targets = new();
    internal readonly TextBox Request = new() { AcceptsReturn = true, MinHeight = 60, MaxHeight = 160, TextWrapping = TextWrapping.Wrap };
    internal readonly TextBox ReviewText = new() { IsReadOnly = true, IsUndoEnabled = false, AcceptsReturn = true, MinHeight = 120, MaxHeight = 300, TextWrapping = TextWrapping.Wrap };
    internal readonly TextBox Result = new() { IsReadOnly = true, IsUndoEnabled = false, AcceptsReturn = true, MinHeight = 120, MaxHeight = 300, TextWrapping = TextWrapping.Wrap };
    internal readonly TextBlock Status = new() { TextWrapping = TextWrapping.Wrap };
    internal event Action? DraftChanged;
    internal event Action? InvocationChanged;
    private readonly Func<bool> eligible;
    private Window? dialog;
    private TaskCompletionSource<bool>? pending;
    private Func<bool>? pendingCurrent;

    internal ObservationWorkflowView(Window owner, Func<string, Task> run, Func<bool> eligible, Func<bool> privateEligible)
    {
        Owner = owner;
        this.eligible = eligible;
        presentation = new(this, owner, () => !disposed && privateEligible());
        presentation.PresentationChanged += (_, _) =>
        {
            if (presentation.IsPresented) { return; }
            ClearReview();
            PresentationLost?.Invoke(this, EventArgs.Empty);
        };
        owner.Closing += OnOwnerClosing;
        owner.Closed += OnOwnerClosed;
        var panel = new StackPanel { Margin = new(12), Spacing = 6 };
        panel.Children.Add(new TextBlock
        {
            Text = "Native-only, one to six explicitly ordered original host observations. No model, scripts, network, probes or settings writes. Save remains disabled; separately review source, original request and Active target, Enable, then Invoke. Each step needs independent approval. Results remain in this private workbench view, not conversation history.",
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
        if (!IsNativePresented || !current()) { throw new InvalidOperationException("The private exact review is no longer current."); }
        if (pending is not null) { throw new InvalidOperationException("Another exact native review is pending."); }
        if (Owner is ResponseWindow primary) { return await ConfirmHosted(primary, title, text, confirm, current, token); }
        var native = CapturePresentation();
        var returnFocus = Owner.FocusManager?.GetFocusedElement() as Control;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending = completion;
        pendingCurrent = () => native() && current();
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
            var nativeClosure = opened.ShowDialog(Owner);
            // Avalonia can finish making a window visible after a reentrant close in Opened.
            if (closed) { opened.Hide(); }
            if (!native() || !current() || token.IsCancellationRequested)
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
            if (native() && current() && !token.IsCancellationRequested
                && returnFocus is not null && ReferenceEquals(TopLevel.GetTopLevel(returnFocus), Owner)
                && returnFocus.IsEffectivelyEnabled && returnFocus.IsEffectivelyVisible)
            { returnFocus.Focus(); }
        }

        void OwnerChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == IsVisibleProperty && !IsNativePresented)
            {
                completion.TrySetResult(false);
                pendingCurrent = null;
                opened.Content = null;
                CloseReview();
            }
        }
    }

    private async Task<bool> ConfirmHosted(ResponseWindow primary, string title, string text, string confirm,
        Func<bool> current, CancellationToken token)
    {
        var native = CapturePresentation();
        var returnFocus = Owner.FocusManager?.GetFocusedElement() as Control;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending = completion;
        pendingCurrent = () => native() && current();
        var content = new TextBox
        {
            Text = text, IsReadOnly = true, IsUndoEnabled = false, AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap, TabIndex = 0,
        };
        AutomationProperties.SetName(content, "Complete exact review");
        var yes = new Button { Content = confirm, TabIndex = 1 };
        var no = new Button { Content = "Cancel", TabIndex = 2 };
        yes.Click += (_, _) => CompleteApproval(true);
        no.Click += (_, _) => CompleteApproval(false);
        var buttons = new WrapPanel { Children = { yes, no } };
        var panel = new DockPanel { Margin = new(12) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        panel.Children.Add(content);
        panel.AddHandler(KeyDownEvent, (_, args) =>
        {
            if (args.Key != Key.Escape) { return; }
            args.Handled = true;
            CompleteApproval(false);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        using var registration = token.Register(() => Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(pending, completion)) { return; }
            pendingCurrent = null;
            content.Text = string.Empty;
            completion.TrySetCanceled(token);
        }));
        try
        {
            primary.ShowNativeReview(panel, title, panel, () => native() && current(), shown =>
            {
                if (shown) { return; }
                pendingCurrent = null;
                content.Text = string.Empty;
                completion.TrySetResult(false);
            });
            content.Focus();
            var approved = await completion.Task;
            return approved && pendingCurrent?.Invoke() == true && !token.IsCancellationRequested;
        }
        finally
        {
            pending = null;
            pendingCurrent = null;
            primary.RemoveNativeReview(panel);
            content.Text = string.Empty;
            panel.Children.Clear();
            if (native() && current() && returnFocus?.IsEffectivelyVisible == true && returnFocus.IsEffectivelyEnabled)
            { returnFocus.Focus(); }
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

    private void OnOwnerClosing(object? sender, WindowClosingEventArgs args) => Closing?.Invoke(this, args);
    private void OnOwnerClosed(object? sender, EventArgs args) => Dispose();

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        ClearReview();
        Owner.Closing -= OnOwnerClosing;
        Owner.Closed -= OnOwnerClosed;
        presentation.Dispose();
        if (Owner is ResponseWindow primary) { primary.RemoveHostedView(this); }
        else if (Owner is ObservationWorkflowWindow) { Owner.Close(); }
    }
}
