using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Kora.Application.Skills;
using Kora.Core.Skills;
using System.Text;

namespace Kora;

internal sealed class OwnedSkillEditorWindow : Window, IOwnedSkillSaveReview, IOwnedSkillDeletionReview
{
    internal readonly TextBox IdInput = new() { PlaceholderText = "canonical-skill-id" };
    internal readonly TextBox VersionInput = new() { Text = "1.0.0" };
    internal readonly TextBox NameInput = new();
    internal readonly TextBox DescriptionInput = new();
    internal readonly TextBox Guidance = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 140 };
    internal readonly TextBox Critique = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 100 };
    internal readonly TextBox Fixture = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 100 };
    internal readonly TextBox Purpose = new() { IsReadOnly = true, AcceptsReturn = true, MaxHeight = 80 };
    internal readonly TextBox ExpectedInput = new() { AcceptsReturn = true, MaxHeight = 80 };
    internal readonly TextBox ExpectedOutput = new() { AcceptsReturn = true, MaxHeight = 80 };
    internal readonly ComboBox Saved = new() { MinWidth = 400 };
    internal readonly ComboBox Tool = new() { ItemsSource = new[] { "No tools (native v1)", "Host WebPageGet only (native v2)" }, SelectedIndex = 0 };
    internal readonly ComboBox InputFormat = new() { ItemsSource = new[] { "Literal guidance (native v1/v2)", "Required literal guidance inputs (native v3; no tools)" }, SelectedIndex = 0 };
    internal readonly TextBox ComponentPath = new() { PlaceholderText = @"references\notes.md" };
    internal readonly TextBox ComponentBody = new() { AcceptsReturn = true, IsUndoEnabled = false, Height = 100 };
    private readonly ComboBox components = new();
    private readonly Dictionary<string, byte[]> referenceContents = new(StringComparer.Ordinal);
    private bool loadingComponent;
    private bool componentDirty;
    internal OwnedDeclarativeSkillSnapshot? BasePackage { get; set; }
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private Window? review;
    private readonly Func<bool> eligible;
    internal event Action? DraftChanged;
    internal event Action? CritiqueChanged;
    internal event Action? FixtureChanged;

    internal OwnedSkillEditorWindow(Action<string> run, Func<bool> eligible)
    {
        this.eligible = eligible;
        Title = "New Kora-owned declarative skill";
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
        Critique.PropertyChanged += (_, args) => { if (args.Property == TextBox.TextProperty) { CritiqueChanged?.Invoke(); } };
        Fixture.PropertyChanged += (_, args) => { if (args.Property == TextBox.TextProperty) { FixtureChanged?.Invoke(); } };
        Tool.SelectionChanged += (_, _) => DraftChanged?.Invoke();
        AutomationProperties.SetName(Tool, "Exact existing host capability");
        fields.Children.Add(new TextBlock { Text = "Host capability declaration (not permission)" });
        fields.Children.Add(Tool);
        InputFormat.SelectionChanged += (_, _) => DraftChanged?.Invoke();
        InputFormat.ItemsSource = new[] { "Literal guidance (native v1/v2)", "Required literal guidance inputs (native v3; no tools)",
            "Declared Markdown component package (native v5; literal; no tools)" };
        AutomationProperties.SetName(InputFormat, "Explicit guidance input format");
        fields.Children.Add(InputFormat);
        AutomationProperties.SetName(Guidance, "Literal declarative workflow guidance");
        fields.Children.Add(new TextBlock
        {
            Text = "Local only. Native v5 adds one to four explicitly authored plain Markdown components; no file imports, scripts or tools. Native v1/v2/v3 retain their existing meaning. Saving the whole package remains disabled; it never invokes or proves harmlessness.",
            TextWrapping = TextWrapping.Wrap,
        });
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var action in new[] { "Create", "Refine", "Cancel refinement", "Validate", "Test", "Cancel test", "Save", "List", "Reopen", "Delete", "Use", "Cancel" })
        {
            var button = new Button { Content = action, Margin = new(3) };
            button.Click += (_, _) => run(action.ToLowerInvariant().Replace(' ', '-'));
            buttons.Children.Add(button);
        }
        fields.Children.Add(buttons);
        foreach (var pair in new[] { ("Original new-skill purpose", Purpose), ("Expected input (or explicitly none)", ExpectedInput),
            ("Expected output", ExpectedOutput) })
        {
            fields.Children.Add(new TextBlock { Text = pair.Item1 });
            AutomationProperties.SetName(pair.Item2, pair.Item1);
            fields.Children.Add(pair.Item2);
            pair.Item2.PropertyChanged += (_, args) => { if (args.Property == TextBox.TextProperty) { DraftChanged?.Invoke(); } };
        }
        var generate = new Button { Content = "Generate initial guidance", Margin = new(3) };
        generate.Click += (_, _) => run("generate");
        fields.Children.Add(generate);
        fields.Children.Add(Saved);
        AutomationProperties.SetName(ComponentPath, "Exact declared package-relative Markdown component path");
        AutomationProperties.SetName(ComponentBody, "Complete literal Markdown component body");
        AutomationProperties.SetName(components, "Declared component tree paths");
        fields.Children.Add(new TextBlock { Text = "Native v5 component tree: manifest.yaml, instructions.md, and exactly these declared Markdown paths" });
        fields.Children.Add(components);
        fields.Children.Add(ComponentPath);
        fields.Children.Add(ComponentBody);
        foreach (var action in new[] { "Apply component", "Remove component" })
        {
            var button = new Button { Content = action };
            button.Click += (_, _) => run(action is "Apply component" ? "apply-component" : "remove-component");
            fields.Children.Add(button);
        }
        components.SelectionChanged += (_, _) =>
        {
            if (components.SelectedItem is not string path) { return; }
            if (componentDirty) { Report("Apply or explicitly remove the current component edit before selecting another component."); return; }
            loadingComponent = true;
            ComponentPath.Text = path;
            ComponentBody.Text = new UTF8Encoding(false, true).GetString(referenceContents[path]);
            loadingComponent = false;
            componentDirty = false;
        };
        foreach (var box in new[] { ComponentPath, ComponentBody })
        {
            box.PropertyChanged += (_, args) =>
            {
                if (!loadingComponent && args.Property == TextBox.TextProperty)
                { componentDirty = true; DraftChanged?.Invoke(); }
            };
        }
        fields.Children.Add(new TextBlock { Text = "Original critique for explicit local guidance refinement" });
        AutomationProperties.SetName(Critique, "Original skill guidance critique");
        fields.Children.Add(Critique);
        fields.Children.Add(new TextBlock
        {
            Text = "SIMULATED native v1 example only. Volatile JSON: input and 1-8 assertions (kind equals/contains, value). No tools or mocked tool responses. Review before explicit local inference; Save/enable remain separate.",
            TextWrapping = TextWrapping.Wrap,
        });
        AutomationProperties.SetName(Fixture, "Volatile simulated skill fixture JSON");
        fields.Children.Add(Fixture);
        DockPanel.SetDock(fields, Dock.Top);
        var editorFields = new ScrollViewer { Content = fields, MaxHeight = 550 };
        DockPanel.SetDock(editorFields, Dock.Top);
        panel.Children.Add(editorFields);
        DockPanel.SetDock(status, Dock.Bottom);
        panel.Children.Add(status);
        panel.Children.Add(Guidance);
        Content = new ScrollViewer { Content = panel };
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
    internal OwnedDeclarativeSkillDocument Document()
    {
        if (componentDirty) { throw new InvalidOperationException("Apply or explicitly remove the edited component before validating the complete tree."); }
        return Build(referenceContents);
    }

    private OwnedDeclarativeSkillDocument Build(Dictionary<string, byte[]> references) =>
        InputFormat.SelectedIndex == 2
        ? Tool.SelectedIndex == 0
            ? OwnedDeclarativeSkillDocument.CreatePackage(IdInput.Text ?? "", VersionInput.Text ?? "",
                NameInput.Text ?? "", DescriptionInput.Text ?? "", Guidance.Text ?? "", references)
            : throw new InvalidDataException("Owned Markdown packages declare tools none.")
        : references.Count != 0 ? throw new InvalidDataException("Select native v5 explicitly or remove every component; old formats cannot silently drop components.")
        : OwnedDeclarativeSkillDocument.Create(
        IdInput.Text ?? "", VersionInput.Text ?? "", NameInput.Text ?? "", DescriptionInput.Text ?? "", Guidance.Text ?? "",
        Tool.SelectedIndex switch
        {
            0 => OwnedSkillToolSelection.None,
            1 => OwnedSkillToolSelection.WebPageGet,
            _ => throw new InvalidOperationException("Select one supported host capability declaration."),
        }, InputFormat.SelectedIndex switch
        {
            0 => OwnedSkillGuidanceFormat.Literal,
            1 => OwnedSkillGuidanceFormat.RequiredLiteralInputs,
            _ => throw new InvalidOperationException("Select one supported guidance input format."),
        });

    internal void ApplyComponent(string path, string body)
    {
        if (InputFormat.SelectedIndex != 2) { throw new InvalidOperationException("Select native v5 explicitly before adding components."); }
        if (componentDirty && (!string.Equals(ComponentPath.Text, path, StringComparison.Ordinal)
            || !string.Equals(ComponentBody.Text, body, StringComparison.Ordinal)))
        { throw new InvalidOperationException("Apply or explicitly remove the current component edit before replacing another component."); }
        var proposed = new Dictionary<string, byte[]>(referenceContents, StringComparer.Ordinal);
        try { proposed[path] = new UTF8Encoding(false, true).GetBytes(body); }
        catch (EncoderFallbackException exception) { throw new InvalidDataException("Component text must be well-formed Unicode.", exception); }
        _ = Build(proposed);
        referenceContents.Clear();
        foreach (var pair in proposed) { referenceContents.Add(pair.Key, pair.Value); }
        RefreshComponents();
    }

    internal void RemoveComponent(string path)
    {
        if (componentDirty && !string.Equals(ComponentPath.Text, path, StringComparison.Ordinal))
        { throw new InvalidOperationException("Apply or explicitly remove the current component edit first."); }
        if (!referenceContents.Remove(path) && !(componentDirty && string.Equals(ComponentPath.Text, path, StringComparison.Ordinal)))
        { throw new InvalidOperationException("Select one exact currently declared component to remove."); }
        RefreshComponents();
    }

    internal void LoadComponents(OwnedDeclarativeSkillSnapshot? snapshot)
    {
        BasePackage = snapshot;
        referenceContents.Clear();
        foreach (var reference in snapshot?.Document.References ?? [])
        { referenceContents.Add(reference.RelativePath, reference.Bytes.ToArray()); }
        RefreshComponents();
    }

    private void RefreshComponents()
    {
        loadingComponent = true;
        components.ItemsSource = referenceContents.Keys.ToArray();
        components.SelectedIndex = -1;
        ComponentPath.Text = "";
        ComponentBody.Text = "";
        loadingComponent = false;
        componentDirty = false;
        DraftChanged?.Invoke();
    }

    private string DescribeTree(OwnedDeclarativeSkillDocument document)
    {
        var before = BasePackage?.Document;
        var old = before is null ? new Dictionary<string, byte[]>(StringComparer.Ordinal)
            : Files(before);
        var current = Files(document);
        var tree = string.Join("\n", old.Keys.Union(current.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(path =>
            !current.TryGetValue(path, out var bytes) ? "REMOVED " + path + " (" + old[path].Length + " bytes)"
                : !old.TryGetValue(path, out var previous) ? "ADDED " + path + " (" + bytes.Length + " bytes)"
                : (previous.AsSpan().SequenceEqual(bytes) ? "UNCHANGED " : "MODIFIED ") + path + " (" + bytes.Length + " bytes)"));
        return "Source: " + (before is null ? "original-user volatile draft" : before.Id + " " + before.Version + " / " + before.RevisionDigest)
            + "\nWhole destination package digest: " + document.RevisionDigest + "\n" + tree
            + "\nComplete proposed package:\n" + document.CompleteText;
    }

    private static Dictionary<string, byte[]> Files(OwnedDeclarativeSkillDocument document)
    {
        var files = document.References.ToDictionary(item => item.RelativePath, item => item.Bytes.ToArray(), StringComparer.Ordinal);
        files.Add("manifest.yaml", document.ManifestBytes.ToArray());
        files.Add("instructions.md", document.InstructionBytes.ToArray());
        return files;
    }

    public async Task<bool> Review(OwnedDeclarativeSkillDocument document, string destination, CancellationToken token) =>
        document.ReferenceDeclaration is null && BasePackage?.Document.ReferenceDeclaration is null
        ? await ChooseOnUi("Review complete skill package",
            "Exact new version directory: " + destination
                + "\nExactly TWO files: manifest.yaml and instructions.md. Local only; references none. Declared capability: " + document.Tool
                + ". Saved inactive/unselected; use requires separate review and enablement. Tool actions still require independent permission.",
            document.ManifestText, document.Guidance, ["Continue to separate native publication confirmation", "Cancel"], token) == 0
        : await ChooseOnUi("Review complete skill package",
            "Exact new version directory: " + destination
                + "\nExact complete declared tree. Local only. Declared capability: " + document.Tool
                + ". Saved inactive/unselected; use requires separate review and enablement. Tool actions still require independent permission.",
            BasePackage?.Document.CompleteText ?? "(no saved source)", DescribeTree(document),
            ["Continue to separate native publication confirmation", "Cancel"], token,
            firstLabel: "Complete original package", secondLabel: "Exact tree changes and complete proposed bytes") == 0;

    public async Task<bool> ConfirmPublication(OwnedDeclarativeSkillDocument document, string destination, CancellationToken token) =>
        document.ReferenceDeclaration is null && BasePackage?.Document.ReferenceDeclaration is null
        ? await ChooseOnUi("Confirm exact new skill publication",
            "Publish this exact reviewed TWO-file package at " + destination
                + "? Existing versions will never be replaced. The saved package remains inactive. Use requires a separate exact-version review and enablement.",
            document.ManifestText, document.Guidance, ["Publish exact new version", "Cancel"], token) == 0
        : await ChooseOnUi("Confirm exact new skill publication",
            "Publish this exact reviewed whole package at " + destination
                + "? Existing versions will never be replaced. The saved package remains inactive. Use requires a separate exact-version review and enablement.",
            BasePackage?.Document.CompleteText ?? "(no saved source)", DescribeTree(document), ["Publish exact new version", "Cancel"], token,
            firstLabel: "Complete original package", secondLabel: "Exact tree changes and complete proposed bytes") == 0;

    internal async Task<bool> ConfirmComponentRead(OwnedDeclarativeSkillSnapshot declaration, CancellationToken token) =>
        await ChooseOnUi("Review exact owned component declaration",
            "Only primary files have been read. Authorize only these declared owned-package Markdown reads; this neither enables nor invokes.",
            declaration.Document.ManifestText, declaration.Document.Guidance,
            ["Read only the exact declared owned Markdown components", "Cancel"], token) == 0;

    public async Task<bool> ReviewDeletion(OwnedSkillDeletionSnapshot source, CancellationToken token) =>
        await ChooseOnUi("Review exact owned skill source deletion", DeletionDisclosure(source),
            source.Source.Document.ManifestText, source.Source.Document.Guidance,
            ["Continue to separate deletion confirmation", "Cancel"], token) == 0;

    public async Task<bool> ConfirmDeletion(OwnedSkillDeletionSnapshot source, CancellationToken token) =>
        await ChooseOnUi("Confirm exact owned skill source deletion", DeletionDisclosure(source),
            source.Source.Document.ManifestText, source.Source.Document.Guidance,
            ["Delete only this exact owned source", "Cancel"], token) == 0;

    private static string DeletionDisclosure(OwnedSkillDeletionSnapshot source) =>
        "Delete owned " + source.Source.Document.Name + " (" + source.Source.Document.Id + " " + source.Source.Document.Version
        + ") at " + source.Destination + "?\nExactly manifest.yaml, instructions.md and their now-empty version directory; the ID/root and all other entries remain."
        + "\nDeclared dependency: " + source.Source.Document.Tool + "; references: none. Future use that needs this exact source fails closed."
        + " External dependents are not exhaustively inventoried. Immutable admitted tasks are not cancelled; independent grants, history and audit are not erased or renewed."
        + "\nNo reference packages, shared/profile originals, bundled sources, arbitrary paths or tree deletion. No backup/rollback, model, tools or egress."
        + " Partial/unconfirmed deletion is held for explicit inspection, never retried automatically.";

    internal async Task<int> DirtyChoice(bool canSave, Func<bool> privateEligible, CancellationToken token)
    {
        var choice = await ChooseOnUi("Unsaved skill draft",
            canSave ? "Save the new version, discard this volatile draft, or cancel and keep it open. No draft is autosaved."
                : "Original authoring authority expired. Saving is unavailable. Explicitly discard this volatile draft or cancel and keep it open.",
            "", "", canSave ? ["Save", "Discard", "Cancel"] : ["Discard", "Cancel"], token, privateEligible);
        return canSave ? choice : choice == 0 ? 1 : 2;
    }

    internal Task<int> ChooseGuidance(string title, string disclosure, string current, string proposed,
        string[] choices, Func<bool> currentInput, CancellationToken token) =>
        ChooseOnUi(title, disclosure, current, proposed, choices, token, currentInput, "Complete current guidance", "Complete proposed guidance");

    internal async Task<bool> ReviewFixture(OwnedSkillFixtureWorkflow.Preview preview, Func<bool> current, CancellationToken token) =>
        await ChooseOnUi("Review SIMULATED skill example",
            "Native v1 only. One local tools-NONE inference after explicit Start. No actual tools, network, effects, grants, history, "
            + "memory, hosted fallback, Save or enablement. Expected assertions stay host-side. Complete source, unchanged fixture and exact "
            + "serialized model input below. Passing is not harmlessness or qualification. Review expires in two minutes.",
            preview.Document.ManifestText + "\n--- instructions.md ---\n" + preview.Document.Guidance + "\n--- EPHEMERAL fixture ---\n" + preview.Fixture.Source,
            preview.Context, ["Start SIMULATED test", "Cancel"], token, current, "Source and fixture (inert data)", "Local tools-none input") == 0;

    internal async Task ShowFixtureResult(OwnedSkillFixtureWorkflow.Result result, Func<bool> current, CancellationToken token) =>
        _ = await ChooseOnUi("SIMULATED skill example result",
            "SIMULATED / " + (result.Passed ? "PASS" : "FAIL")
            + ". Example behavior only; not a real observation, harmlessness, qualification or enablement. Volatile; never saved as conversation facts. "
            + "Each assertion uses case-sensitive ordinal matching. Nothing saved, enabled or granted.",
            result.Preview.Document.ManifestText + "\n--- instructions.md ---\n" + result.Preview.Document.Guidance
                + "\n--- EPHEMERAL fixture ---\n" + result.Preview.Fixture.Source + "\n--- EXACT LOCAL INPUT ---\n" + result.Preview.Context,
            result.Output, ["Dismiss simulated result"], token, current, "Exact tested source and fixture", "Exact simulated output",
            System.Text.Json.JsonSerializer.Serialize(result.Assertions));

    internal Task<int> ChooseInitial(string title, string disclosure, string manifest, string guidance,
        string[] choices, Func<bool> currentInput, CancellationToken token) =>
        ChooseOnUi(title, disclosure, manifest, guidance, choices, token, currentInput);

    private async Task<int> ChooseOnUi(string title, string disclosure, string manifest, string instructions,
        string[] choices, CancellationToken token, Func<bool>? privateEligible = null,
        string firstLabel = "manifest.yaml", string secondLabel = "instructions.md", string? assertionResults = null)
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        { return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ChooseOnUi(title, disclosure, manifest, instructions, choices, token, privateEligible, firstLabel, secondLabel, assertionResults)); }
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
        foreach (var pair in new[] { (firstLabel, manifest), (secondLabel, instructions) })
        {
            files.Children.Add(new TextBlock { Text = pair.Item1 });
            var source = new TextBox { Text = pair.Item2, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
            AutomationProperties.SetName(source, "Complete exact " + pair.Item1);
            files.Children.Add(source);
        }
        if (assertionResults is not null)
        {
            files.Children.Add(new TextBlock { Text = "Host assertion results (not model output)" });
            var assertions = new TextBox { Text = assertionResults, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
            AutomationProperties.SetName(assertions, "Complete exact host assertion results");
            files.Children.Add(assertions);
        }
        panel.Children.Add(new ScrollViewer { Content = files });
        dialog.Content = panel;
        review = dialog;
        dialog.KeyDown += (_, key) =>
        {
            if (key.Key == Avalonia.Input.Key.Escape) { key.Handled = true; dialog.Close(); }
        };
        dialog.Closed += (_, _) => { if (ReferenceEquals(review, dialog)) { review = null; } result.TrySetResult(-1); };
        using var cancellation = token.Register(() => Avalonia.Threading.Dispatcher.UIThread.Post(dialog.Close));
        await dialog.ShowDialog(this);
        var choice = await result.Task;
        // Owner Hide/Close can complete the dialog before updating the owner's own visibility.
        return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
            () => IsVisible && current() && !token.IsCancellationRequested ? choice : -1);
    }
}
