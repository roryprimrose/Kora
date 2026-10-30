using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
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
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        { return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => Confirm(title, text, confirm, current, token)); }
        token.ThrowIfCancellationRequested();
        if (!current()) { throw new InvalidOperationException("The private exact review is no longer current."); }
        if (pending is not null) { throw new InvalidOperationException("Another exact native review is pending."); }
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending = completion;
        pendingCurrent = current;
        var opened = new Window { Title = title, Width = 780, Height = 650 };
        dialog = opened;
        var content = new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(content, "Complete exact review");
        var yes = new Button { Content = confirm };
        var no = new Button { Content = "Cancel" };
        yes.Click += (_, _) => CompleteApproval(true);
        no.Click += (_, _) => CompleteApproval(false);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { yes, no } };
        var panel = new DockPanel { Margin = new(12) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        panel.Children.Add(content);
        opened.Content = panel;
        opened.Closed += (_, _) => completion.TrySetResult(false);
        return await Wait();

        async Task<bool> Wait()
        {
            using var registration = token.Register(() => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            { completion.TrySetCanceled(token); opened.Close(); }));
            try
            {
                opened.Show(this);
#pragma warning disable VSTHRD003 // The controller owns the native review until completion/cancellation joins it.
                return await completion.Task && current() && !token.IsCancellationRequested;
#pragma warning restore VSTHRD003
            }
            finally { opened.Close(); pending = null; pendingCurrent = null; dialog = null; }
        }
    }

    internal void CompleteApproval(bool approved)
    {
        if (pending is null) { throw new InvalidOperationException("No exact native review is pending."); }
        pending.TrySetResult(approved && pendingCurrent?.Invoke() == true);
    }

    internal void ClearReview()
    {
        pending?.TrySetResult(false);
        dialog?.Close();
        ReviewText.Text = "";
        Result.Text = "";
    }
}
