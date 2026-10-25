using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Kora.Application.Skills;
using Kora.Core.Skills;

namespace Kora;

internal sealed class OwnedSkillEditorWindow : Window, IOwnedSkillSaveReview
{
    internal readonly TextBox IdInput = new() { PlaceholderText = "canonical-skill-id" };
    internal readonly TextBox VersionInput = new() { Text = "1.0.0" };
    internal readonly TextBox NameInput = new();
    internal readonly TextBox DescriptionInput = new();
    internal readonly TextBox Guidance = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
    internal readonly ComboBox Saved = new() { MinWidth = 400 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private Window? review;
    private readonly Func<bool> eligible;
    internal event Action? DraftChanged;

    internal OwnedSkillEditorWindow(Action<string> run, Func<bool> eligible)
    {
        this.eligible = eligible;
        Title = "New Kora-owned instruction-only skill";
        Width = 900;
        Height = 700;
        var panel = new DockPanel { Margin = new(12) };
        var fields = new StackPanel { Spacing = 4 };
        foreach (var pair in new[] { ("Skill ID", IdInput), ("New version", VersionInput), ("Readable name", NameInput),
            ("Description", DescriptionInput) })
        {
            fields.Children.Add(new TextBlock { Text = pair.Item1 });
            AutomationProperties.SetName(pair.Item2, pair.Item1);
            fields.Children.Add(pair.Item2);
            pair.Item2.PropertyChanged += (_, args) => { if (args.Property == TextBox.TextProperty) { DraftChanged?.Invoke(); } };
        }
        Guidance.PropertyChanged += (_, args) => { if (args.Property == TextBox.TextProperty) { DraftChanged?.Invoke(); } };
        AutomationProperties.SetName(Guidance, "Literal instruction-only workflow guidance");
        fields.Children.Add(new TextBlock
        {
            Text = "Local only. No tools, references, scripts, model generation or imports. Saving creates an immutable inactive version; it does not enable or invoke anything.",
            TextWrapping = TextWrapping.Wrap,
        });
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var action in new[] { "Create", "Validate", "Save", "List", "Reopen", "Use", "Cancel" })
        {
            var button = new Button { Content = action, Margin = new(3) };
            button.Click += (_, _) => run(action.ToLowerInvariant());
            buttons.Children.Add(button);
        }
        fields.Children.Add(buttons);
        fields.Children.Add(Saved);
        DockPanel.SetDock(fields, Dock.Top);
        panel.Children.Add(fields);
        DockPanel.SetDock(status, Dock.Bottom);
        panel.Children.Add(status);
        panel.Children.Add(Guidance);
        Content = panel;
    }

    internal void Report(string message) => status.Text = message;
    internal bool ShowForPrivateRecovery(Func<bool> privateEligible)
    {
        if (!privateEligible()) { return false; }
        Show();
        Activate();
        return true;
    }
    internal void RetireReview() => review?.Close();
    internal OwnedDeclarativeSkillDocument Document() => OwnedDeclarativeSkillDocument.Create(
        IdInput.Text ?? "", VersionInput.Text ?? "", NameInput.Text ?? "", DescriptionInput.Text ?? "", Guidance.Text ?? "");

    public async Task<bool> Review(OwnedDeclarativeSkillDocument document, string destination, CancellationToken token) =>
        await ChooseOnUi("Review complete skill package",
            "Exact new version directory: " + destination
                + "\nExactly TWO files: manifest.yaml and instructions.md. Local only; tools none; references none. Saved inactive/unselected; use requires separate review and enablement.",
            document.ManifestText, document.Guidance, ["Continue to separate native publication confirmation", "Cancel"], token) == 0;

    public async Task<bool> ConfirmPublication(OwnedDeclarativeSkillDocument document, string destination, CancellationToken token) =>
        await ChooseOnUi("Confirm exact new skill publication",
            "Publish this exact reviewed TWO-file package at " + destination
                + "? Existing versions will never be replaced. The saved package remains inactive. Use requires a separate exact-version review and enablement.",
            document.ManifestText, document.Guidance, ["Publish exact new version", "Cancel"], token) == 0;

    internal async Task<int> DirtyChoice(bool canSave, Func<bool> privateEligible, CancellationToken token)
    {
        var choice = await ChooseOnUi("Unsaved skill draft",
            canSave ? "Save the new version, discard this volatile draft, or cancel and keep it open. No draft is autosaved."
                : "Original authoring authority expired. Saving is unavailable. Explicitly discard this volatile draft or cancel and keep it open.",
            "", "", canSave ? ["Save", "Discard", "Cancel"] : ["Discard", "Cancel"], token, privateEligible);
        return canSave ? choice : choice == 0 ? 1 : 2;
    }

    private async Task<int> ChooseOnUi(string title, string disclosure, string manifest, string instructions,
        string[] choices, CancellationToken token, Func<bool>? privateEligible = null)
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        { return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ChooseOnUi(title, disclosure, manifest, instructions, choices, token, privateEligible)); }
        var current = privateEligible ?? eligible;
        if (!current() || token.IsCancellationRequested) { return -1; }
        var result = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var dialog = new Window { Title = title, Width = 850, Height = 650 };
        var panel = new DockPanel { Margin = new(12) };
        var label = new TextBlock { Text = disclosure, TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(label, Dock.Top);
        panel.Children.Add(label);
        var buttons = new WrapPanel();
        for (var index = 0; index < choices.Length; index++)
        {
            var value = index;
            var button = new Button { Content = choices[index], Margin = new(4) };
            button.Click += (_, _) => { result.TrySetResult(current() ? value : -1); dialog.Close(); };
            buttons.Children.Add(button);
        }
        DockPanel.SetDock(buttons, Dock.Bottom);
        panel.Children.Add(buttons);
        var files = new StackPanel { Spacing = 8 };
        foreach (var pair in new[] { ("manifest.yaml", manifest), ("instructions.md", instructions) })
        {
            files.Children.Add(new TextBlock { Text = pair.Item1 });
            var source = new TextBox { Text = pair.Item2, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
            AutomationProperties.SetName(source, "Complete exact " + pair.Item1);
            files.Children.Add(source);
        }
        panel.Children.Add(new ScrollViewer { Content = files });
        dialog.Content = panel;
        review = dialog;
        dialog.Closed += (_, _) => { if (ReferenceEquals(review, dialog)) { review = null; } result.TrySetResult(-1); };
        using var cancellation = token.Register(() => Avalonia.Threading.Dispatcher.UIThread.Post(dialog.Close));
        dialog.Show(this);
        return await result.Task;
    }
}
