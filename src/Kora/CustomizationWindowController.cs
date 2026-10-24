using System.ComponentModel;
using System.Text;

using Avalonia.Controls;
using Avalonia.Threading;

using Kora.Application.Definitions;
using Kora.Application.ViewModels;
using Kora.Core.Artifacts;
using Kora.Core.Customizations;
using Kora.Core.Hosting;
using Kora.Core.Diagnostics;
using Kora.Definitions.Skills;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class CustomizationWindowController(
    MainViewModel main, CustomizationManagementService management, ICustomizationAuthoring authoring,
    ArtifactCatalogue catalogue, ArtifactCatalogueReloadService reload,
    Func<bool> ownsHost, Func<long> ownershipRevision, ILogger<CustomizationWindowController> logger) : IDisposable
{
    private CustomizationWindow? window;
    private CancellationTokenSource? lifetime;
    private IReadOnlyList<CustomizationSource> sources = [];
    private readonly Dictionary<string, CustomizationSnapshot> bundled = new(StringComparer.Ordinal);
    private CustomizationSource? source;
    private CustomizationSnapshot? baseline;
    private List<CustomizationFile> draft = [];
    private string? selectedFile;
    private bool changingBuffer;
    private bool busy;
    private bool closing;
    private bool disposed;
    private long ownerRevision;
    private long draftRevision;
    private Task active = Task.CompletedTask;
    private CustomizationCommand? pendingCommand;
    private CustomizationVoiceTarget? voiceTarget;
    private (CustomizationVoiceTarget Target, string Text, Func<bool> Current)? voiceRequest;
    private bool voiceHandoff;
    internal CustomizationVoiceTarget? VoiceTarget => voiceTarget;

    internal async Task WaitForOperation()
    {
#pragma warning disable VSTHRD003 // Observes only the operation owned by this controller; no replacement is started.
        await active;
#pragma warning restore VSTHRD003
    }

    internal void Bind()
    {
        main.CustomizationRequested += Route;
        main.PrivacyClosureRequested += OnPrivacyClosed;
        main.PropertyChanged += OnMainChanged;
        management.UseChanged += OnUseChanged;
        catalogue.Changed += OnUseChanged;
        main.BindCustomizationShutdown(PrepareClose);
        main.BindCustomizationVoice(RouteVoice);
    }

    private bool Eligible() => !disposed && window is not null && ownsHost()
        && ownerRevision == ownershipRevision() && main.CanRevealPrivatePresentation
        && main.CaptureInstructionEditorEligibility()();

    internal void Open()
    {
        if (disposed) { return; }
        if (!ownsHost() || !main.CaptureInstructionEditorEligibility()())
        { main.ReportHostInteractionFailure("Customizations requires the current unlocked owning host and eligible privacy/call state."); return; }
        if (window is not null)
        {
            if (!Eligible()) { OnPrivacyClosed(this, EventArgs.Empty); return; }
            window.Activate(); return;
        }
        ownerRevision = ownershipRevision();
        lifetime = new();
        window = new(Start, main);
        window.Files.SelectionChanged += (_, _) => SelectFile();
        window.Editor.PropertyChanged += (_, change) =>
        {
            if (change.Property == TextBox.TextProperty && !changingBuffer) { InputChanged(); }
        };
        window.Request.PropertyChanged += (_, change) =>
        { if (change.Property == TextBox.TextProperty) { InputChanged(); } };
        window.NewName.PropertyChanged += (_, change) =>
        { if (change.Property == TextBox.TextProperty) { InputChanged(); } };
        window.Destination.SelectionChanged += (_, _) => InputChanged();
        window.PresentationLost += RetireVoiceTarget;
        window.Activated += (_, _) => PublishVoiceTarget();
        window.Closing += OnClosing;
        window.Closed += (_, _) => ClearWindow();
        window.Show();
        Start("Refresh");
    }

    private void Start(string action)
    {
        RetireVoiceTarget();
        if (action is "Filter") { RenderSources(); PublishVoiceTarget(); return; }
        if (busy || disposed) { window?.Status.Text = "Wait for the current operation or cancel its review."; return; }
        active = Run(action);
    }

    private async Task Run(string action)
    {
        if (window is not { } opened || lifetime is null) { return; }
        busy = true;
        var token = lifetime.Token;
        var call = main.CallPolicyRevision;
        var originalInput = main.CaptureInstructionEditorEligibility();
        bool Current() => ReferenceEquals(window, opened) && Eligible() && originalInput() && main.CallPolicyRevision == call;
        try
        {
            if (!Current()) { throw new InvalidOperationException("Customization privacy, host or call admission changed."); }
            using var activity = HostActivity.BeginOperation(Kora.Core.Diagnostics.HostActivityLayer.Desktop,
                Kora.Core.Diagnostics.HostOperation.Request, HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi);
            switch (action)
            {
                case "Voice": await VoiceCore(Current, token); break;
                case "Command": await RouteCore(pendingCommand!, Current, token); break;
                case "Refresh": await Refresh(Current, token); break;
                case "Select":
                    if (opened.SelectedSource is { } selected && await LeaveDraft(Current, token)) { await Inspect(selected, false, Current, token); }
                    break;
                case "Create":
                    if (await LeaveDraft(Current, token)) { await Create(opened.Scope, Current, token); }
                    break;
                case "Create Kora copy": await Copy(Current, token); break;
                case "Review and save": await Save(Current, token); break;
                case "Reload":
                    if (source is not null && await LeaveDraft(Current, token)) { await Inspect(source, false, Current, token); }
                    break;
                case "Review recovery":
                    if (source is not null && await LeaveDraft(Current, token)) { await Inspect(source, true, Current, token); }
                    else if (opened.SelectedSource is { } recovery && await LeaveDraft(Current, token)) { await Inspect(recovery, true, Current, token); }
                    break;
                case "Disable": case "Enable":
                    await ChangeUse(action is "Disable", Current, token); break;
                case "Refine": await Refine(Current, token); break;
                case "Add file": case "Rename file": case "Remove file": EditComponent(action); break;
            }
            activity.Complete(Kora.Core.Diagnostics.HostOperationOutcome.Completed);
        }
        catch (OperationCanceledException)
        { if (ReferenceEquals(window, opened)) { opened.Status.Text = "Operation cancelled. Any attempted publication must be inspected; recovery may be required."; } }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException
            or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            CustomizationFailed(logger, exception.GetType().Name);
            if (ReferenceEquals(window, opened)) { opened.Status.Text = exception.Message; }
            else { main.ReportHostInteractionFailure("Customization operation did not complete: " + exception.GetType().Name); }
        }
        finally { busy = false; voiceRequest = null; PublishVoiceTarget(); }
    }

    private void InputChanged()
    {
        draftRevision++;
        if (!voiceHandoff) { RetireVoiceTarget(); }
        window?.CancelReview();
        if (!voiceHandoff) { PublishVoiceTarget(); }
    }

    private void RetireVoiceTarget()
    {
        voiceTarget?.Retire();
        voiceTarget = null;
        main.PublishCustomizationVoiceTarget(null);
        window?.SetVoiceCaptureAvailable(false);
        if (voiceRequest is not null) { window?.CancelReview(); }
        if (window is { } opened) { opened.VoiceContext.Text = "Voice context unavailable. Focus one current artifact and activate again."; }
    }

    private void PublishVoiceTarget()
    {
        if (busy || window is not { IsActive: true } opened || source is null || !Eligible()) { return; }
        if (voiceTarget is { IsCurrent: true }) { return; }
        var input = main.CaptureCustomizationVoiceEligibility();
        var owner = ownerRevision;
        var target = new CustomizationVoiceTarget(new(source.Kind, source.Scope, source.Name, selectedFile,
            source.IsReadOnly, baseline?.RecoveryDetails is not null),
            () => input() && ownsHost() && owner == ownershipRevision());
        voiceTarget = target;
        main.PublishCustomizationVoiceTarget(target);
        opened.SetVoiceCaptureAvailable(!source.IsReadOnly && baseline?.RecoveryDetails is null);
        opened.VoiceContext.Text = "Voice context: " + source.Kind + " / " + source.Scope + " / " + source.Name
            + "\n" + source.Location + "\nActivate voice to request a proposal; this never saves or enables it.";
    }

    internal async Task RouteVoice(CustomizationVoiceTarget target, string text, Func<bool> current)
    {
        if (busy || !ReferenceEquals(target, voiceTarget) || !target.IsCurrent || !current())
        { main.ReportHostInteractionFailure("The original editor voice target is no longer current. No session received this input."); return; }
        voiceRequest = (target, text, current);
        active = Run("Voice");
#pragma warning disable VSTHRD003 // Join this controller's exact voice operation, including delayed inference release.
        await active;
#pragma warning restore VSTHRD003
    }

    private async Task VoiceCore(Func<bool> current, CancellationToken token)
    {
        var request = voiceRequest!.Value;
        bool Exact() => current() && request.Current() && request.Target.IsCurrent;
        RequireCurrent(Exact, token);
        if (source is null || source.IsReadOnly || baseline?.RecoveryDetails is not null)
        { throw new InvalidOperationException("Voice authoring needs an editable artifact without pending recovery. Built-ins require an explicitly chosen Kora copy."); }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        token = deadline.Token;
        var metadata = System.Text.Json.JsonSerializer.Serialize(request.Target.Context);
        if (await window!.Review("Review editor voice routing disclosure",
            "LOCAL ROUTING ONLY. Send exactly this recognized request and editor metadata to the local model."
            + "\nNo draft files, session conversations, tools or save authority are supplied.\n\nREQUEST\n" + request.Text + "\n\nMETADATA\n" + metadata,
            token, "Interpret editor intent", "Cancel") is not "Interpret editor intent") { return; }
        RequireCurrent(Exact, token);
        var intent = await authoring.Interpret(request.Target.Context, request.Text, Exact, token);
        RequireCurrent(Exact, token);
        if (intent is CustomizationVoiceIntent.Clarify)
        {
            var choice = await window.Review("Choose voice target",
                "Did you mean this artifact?\n" + source.Kind + " / " + source.Scope + " / " + source.Name
                + "\n" + source.Location
                + "\n\nREQUEST\n" + request.Text
                + "\n\nChoose only an artifact refinement or discussion. Nothing has been sent to another session."
                + "\nFor another artifact, a session, a New request, literal dictation or mixed destinations, cancel and explicitly address the intended target.",
                token, "Refine this artifact", "Discuss this artifact", "Cancel");
            RequireCurrent(Exact, token);
            if (choice is "Cancel") { return; }
            intent = choice is "Discuss this artifact" ? CustomizationVoiceIntent.Discuss : CustomizationVoiceIntent.Edit;
        }
        voiceHandoff = true;
        try { window.Request.Text = request.Text; }
        finally { voiceHandoff = false; }
        RequireCurrent(Exact, token);
        await Refine(Exact, token, new(request.Target.Context.Component,
            intent is CustomizationVoiceIntent.Discuss ? CustomizationAuthoringMode.Discuss : CustomizationAuthoringMode.Edit));
    }

    private async Task Refresh(Func<bool> current, CancellationToken token)
    {
        var disk = await management.List(current, token);
        bundled.Clear();
        foreach (var package in EmbeddedSkillCatalogue.Load().Packages)
        {
            var primary = package.Manifest.Files.Single(file => file.Kind is "instructions").Name;
            var item = new CustomizationSource(package.Manifest.Id, CustomizationKind.Skill, CustomizationScope.BuiltIn,
                package.Manifest.Name, "Bundled with Kora", primary, true);
            bundled.Add(item.Id, new(item, package.DeclaredResourceDigest,
                package.Files.Select(file => new CustomizationFile(file.Name, file.Bytes)).ToArray()));
        }
        sources = [.. bundled.Values.Select(snapshot => snapshot.Source), .. disk];
        RenderSources();
        if (window is { } opened) { opened.Status.Text = "Select an artifact to inspect its source. Disabled artifacts remain visible. Saving never enables changed content."; }
    }

    private void RenderSources() => window?.ShowSources(sources, management.IsDisabled, _ => Start("Select"));

    private async Task Inspect(CustomizationSource selected, bool recovery, Func<bool> current, CancellationToken token)
    {
        source = selected;
        baseline = null; draft = []; selectedFile = null;
        ShowDraft();
        var snapshot = selected.Scope == CustomizationScope.BuiltIn ? bundled[selected.Id]
            : await management.Read(selected, recovery, current, token);
        RequireCurrent(current, token);
        baseline = snapshot; draft = snapshot.Files.ToList(); draftRevision++;
        ShowDraft();
        if (recovery && window is { } opened)
        { await opened.Review("Review interrupted transaction", snapshot.RecoveryDetails!, token, "Close review"); }
    }

    private async Task Create(CustomizationScope scope, Func<bool> current, CancellationToken token)
    {
        var opened = window!;
        var name = opened.NewName.Text?.Trim() ?? string.Empty;
        if (name.Length == 0)
        { throw new InvalidDataException("Choose a new lowercase artifact ID and destination, then select Add. You can discuss its behavior with Kora before saving."); }
        var created = await management.Create(opened.Kind, scope, name, current, token);
        RequireCurrent(current, token);
        source = created;
        baseline = null;
        draft = [new(source.PrimaryFile, ArtifactFileDocument.Encode(CustomizationLayout.Template(source.Kind, name), false))];
        selectedFile = null; draftRevision++; ShowDraft();
    }

    private void FlushBuffer()
    {
        if (selectedFile is null || window is null || source?.IsReadOnly != false) { return; }
        var index = draft.FindIndex(file => string.Equals(file.Name, selectedFile, StringComparison.Ordinal));
        if (index >= 0) { draft[index] = draft[index].WithText(window.Editor.Text ?? string.Empty); }
    }

    private void SelectFile()
    {
        if (changingBuffer || window is null) { return; }
        RetireVoiceTarget();
        draftRevision++;
        try
        {
            FlushBuffer();
            selectedFile = window.Files.SelectedItem as string;
            changingBuffer = true;
            window.Editor.Text = draft.SingleOrDefault(file => string.Equals(file.Name, selectedFile, StringComparison.Ordinal))?.Text ?? string.Empty;
            window.FileName.Text = selectedFile;
            window.SetSource(source, baseline?.RecoveryDetails is not null);
        }
        catch (InvalidDataException exception)
        {
            changingBuffer = true;
            window.Files.SelectedItem = selectedFile;
            window.Status.Text = exception.Message;
        }
        finally { changingBuffer = false; PublishVoiceTarget(); }
    }

    private void ShowDraft()
    {
        if (window is not { } opened) { return; }
        changingBuffer = true;
        try
        {
            opened.Files.ItemsSource = draft.Select(file => file.Name).ToArray();
            opened.Files.SelectedItem = selectedFile is not null && draft.Any(file => string.Equals(file.Name, selectedFile, StringComparison.Ordinal))
                ? selectedFile : source?.PrimaryFile;
            selectedFile = opened.Files.SelectedItem as string;
            opened.Editor.Text = draft.SingleOrDefault(file => string.Equals(file.Name, selectedFile, StringComparison.Ordinal))?.Text ?? string.Empty;
            opened.SetSource(source, baseline?.RecoveryDetails is not null);
            opened.FileName.Text = selectedFile;
            var artifact = source is null ? null : management.ObservedArtifact(source);
            opened.Details.Text = source is null ? "Select an artifact"
                : source.Name + " | " + (source.Scope == CustomizationScope.BuiltIn ? "Built-in (read-only)" : source.Scope.ToString())
                    + " | " + (management.IsDisabled(source) ? "Disabled in Kora" : "Not excluded")
                    + "\n" + source.Location + "\n"
                    + (artifact?.IsUseEnabled == true ? "Available through its existing admitted runtime route."
                        : "Use may still require source registration, revision review, compatibility, authentication or a qualified runtime.")
                    + (baseline?.RecoveryDetails is not null ? "\nRecovery required. Review and save resumes only the exact staged proposal." : string.Empty);
        }
        finally { changingBuffer = false; }
    }

    private bool Dirty()
    {
        FlushBuffer();
        return draft.Count > 0 && source?.IsReadOnly == false && (baseline is null || draft.Count != baseline.Files.Count
            || draft.Any(file => !baseline.Files.Any(original => string.Equals(original.Name, file.Name, StringComparison.Ordinal)
                && string.Equals(original.Digest, file.Digest, StringComparison.Ordinal))));
    }

    private async Task<bool> LeaveDraft(Func<bool> current, CancellationToken token)
    {
        if (!Dirty()) { return true; }
        var choice = await window!.Review("Unsaved customization", "Save the complete draft, discard it, or keep editing?", token, "Save", "Discard", "Cancel");
        if (!current()) { return false; }
        return choice is "Discard" || choice is "Save" && await Save(current, token);
    }

    private async Task<bool> Save(Func<bool> current, CancellationToken token)
    {
        if (source is null || source.IsReadOnly) { throw new InvalidOperationException("Select an editable artifact or create a Kora copy."); }
        FlushBuffer();
        var revision = draftRevision;
        var review = ReviewText();
        bool Exact() => current() && draftRevision == revision;
        var saved = await management.Save(source, baseline, draft.ToArray(), async admitted =>
            await window!.Review("Review complete definition changes", review, admitted, "Save disabled", "Cancel") is "Save disabled",
            Exact, token);
        if (saved is null) { return false; }
        RequireCurrent(current, token);
        baseline = saved; draft = saved.Files.ToList();
        var reloaded = reload.ReloadNow();
        await Refresh(current, token);
        RequireCurrent(current, token);
        ShowDraft(); RenderSources();
        window!.Status.Text = reloaded
            ? "Saved and read back. Runtime use remains disabled; review enablement separately. No process, connection or script was started."
            : "Saved and read back, but catalogue reload failed. Runtime use remains disabled. Resolve the source error before enabling.";
        return true;
    }

    private string ReviewText()
    {
        var result = new StringBuilder("Destination: " + source!.Scope + " | " + source.Location
            + "\nSaving leaves this artifact disabled in Kora and grants no execution or connection authority.\n"
            + "Multi-file updates are recoverable, not atomic; other applications may observe intermediate files.\n\n");
        if (baseline?.RecoveryDetails is { } recovery) { result.AppendLine(recovery); }
        foreach (var name in draft.Select(file => file.Name).Union(baseline?.Files.Select(file => file.Name) ?? [], StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var before = baseline?.Files.SingleOrDefault(file => string.Equals(file.Name, name, StringComparison.Ordinal));
            var after = draft.SingleOrDefault(file => string.Equals(file.Name, name, StringComparison.Ordinal));
            result.AppendLine("===== " + name + (before is null ? " [Added]" : after is null ? " [Removed]"
                : string.Equals(before.Digest, after.Digest, StringComparison.Ordinal) ? " [Unchanged]" : " [Modified]") + " =====");
            if (before is not null) { result.AppendLine("ORIGINAL\n" + before.Text); }
            if (after is not null) { result.AppendLine("PROPOSED\n" + after.Text); }
        }
        return result.ToString();
    }

    private void EditComponent(string action)
    {
        if (source is not { Kind: CustomizationKind.Skill, IsReadOnly: false } || baseline?.RecoveryDetails is not null)
        { throw new InvalidOperationException("File components can be edited only in a writable skill draft without pending recovery."); }
        FlushBuffer();
        var name = CustomizationFile.ValidateName(window!.FileName.Text ?? string.Empty);
        var updated = draft.ToList();
        if (action is "Add file") { updated.Add(new(name, [])); }
        else
        {
            if (selectedFile is null || string.Equals(selectedFile, source.PrimaryFile, StringComparison.Ordinal))
            { throw new InvalidOperationException("The primary definition file cannot be removed or renamed."); }
            var original = draft.Single(file => string.Equals(file.Name, selectedFile, StringComparison.Ordinal));
            updated.Remove(original);
            if (action is "Rename file") { updated.Add(new(name, original.CopyBytes())); }
        }
        _ = CustomizationSnapshot.ValidateFiles(source, updated);
        draft = updated;
        draftRevision++; selectedFile = action is "Remove file" ? source.PrimaryFile : name; ShowDraft();
    }

    private async Task ChangeUse(bool disabled, Func<bool> current, CancellationToken token)
    {
        var selected = source ?? window!.SelectedSource ?? throw new InvalidOperationException("Select one exact artifact.");
        if (await management.SetDisabled(selected, disabled, async (snapshot, admitted) =>
            await window!.Review(disabled ? "Disable in Kora" : "Allow Kora use", selected.Name + "\n" + selected.Location
                + (disabled ? "\nExclude from new model context, options, capabilities and invocation; keep files."
                    : "\nRemove only Kora's exclusion. This does not approve changed content, grant tools, authenticate or start an MCP server.")
                + (snapshot is null ? string.Empty : "\n\n" + string.Join("\n\n", snapshot.Files.Select(file => file.Name + "\n" + file.Text))),
                admitted, disabled ? "Disable" : "Allow", "Cancel") is "Disable" or "Allow", current, token))
        { ShowDraft(); RenderSources(); }
    }

    private async Task Refine(Func<bool> current, CancellationToken token, CustomizationAuthoringContext? context = null)
    {
        if (source is null || source.IsReadOnly || baseline?.RecoveryDetails is not null)
        { throw new InvalidOperationException("Open an editable draft without pending recovery. Built-ins require a Kora copy."); }
        FlushBuffer();
        var revision = draftRevision;
        var request = window!.Request.Text ?? string.Empty;
        var frozen = draft.ToArray();
        context ??= new(selectedFile);
        bool Exact() => current() && draftRevision == revision;
        var disclosure = "LOCAL AUTHORING ONLY. This explicitly discloses the following draft and request to the admitted local model, even if runtime use is disabled."
            + "\nNo tool capabilities, hosted transfer, save or enablement are authorized."
            + "\nSELECTED COMPONENT\n" + (context.Component ?? "(whole artifact)")
            + "\nAUTHORING MODE\n" + (context.Mode is CustomizationAuthoringMode.Discuss ? "Discussion only; files unchanged"
                : context.Mode is CustomizationAuthoringMode.Edit ? "Edit proposal" : "Follow original request")
            + "\n\nREQUEST\n" + request + "\n\n"
            + string.Join("\n\n", frozen.Select(file => "===== " + file.Name + " =====\n" + file.Text));
        if (await window.Review("Review model authoring disclosure", disclosure, token, "Send for local authoring", "Cancel") is not "Send for local authoring" || !Exact()) { return; }
        var result = await authoring.Refine(source, frozen, context, request, Exact, token);
        if (!Exact()) { throw new InvalidOperationException("The draft changed during generation. Late output was not applied."); }
        if (context.Mode is CustomizationAuthoringMode.Discuss && (result.Files.Count != frozen.Length || result.Files.Any(file =>
            !frozen.Any(original => string.Equals(original.Name, file.Name, StringComparison.Ordinal)
                && string.Equals(original.Digest, file.Digest, StringComparison.Ordinal)))))
        { throw new InvalidDataException("Discussion returned changed files. The draft is unchanged; request a separate refinement."); }
        var previous = draft;
        draft = result.Files.ToList();
        var proposal = result.Explanation + "\n\n" + ReviewText();
        draft = previous;
        if (await window.Review("Review proposed draft", proposal, token, "Use proposed draft", "Cancel") is "Use proposed draft" && Exact())
        { draft = result.Files.ToList(); draftRevision++; ShowDraft(); window.Status.Text = "Proposal applied to the draft only. Review and save is still required."; }
    }

    private async Task Copy(Func<bool> current, CancellationToken token)
    {
        if (source is null) { throw new InvalidOperationException("Inspect one exact source to copy."); }
        FlushBuffer();
        var original = source;
        var content = draft.ToArray();
        var builtin = source.Scope == CustomizationScope.BuiltIn ? management.ObservedArtifact(source) : null;
        var name = window!.NewName.Text?.Trim() ?? string.Empty;
        var destination = await management.Create(source.Kind, CustomizationScope.Kora, name, current, token);
        RequireCurrent(current, token);
        if (original.IsReadOnly)
        {
            var header = CustomizationLayout.Template(source.Kind, name);
            content = [new(destination.PrimaryFile, ArtifactFileDocument.Encode(header[..header.IndexOf("Write the guidance here.", StringComparison.Ordinal)]
                + (builtin?.Content ?? throw new InvalidOperationException("Bundled declarative instructions are unavailable. No executable files were copied.")), false))];
        }
        else if (source.Kind != CustomizationKind.McpServer)
        {
            var primary = content.Single(file => string.Equals(file.Name, original.PrimaryFile, StringComparison.Ordinal));
            var text = RenameDeclaredName(primary.Text, name);
            content = content.Where(file => !ReferenceEquals(file, primary))
                .Append(new CustomizationFile(destination.PrimaryFile, ArtifactFileDocument.Encode(text, primary.HasBom))).ToArray();
        }
        if (!await LeaveDraft(current, token)) { return; }
        source = destination; baseline = null; draft = content.ToList(); selectedFile = null; draftRevision++; ShowDraft();
    }

    private static string RenameDeclaredName(string text, string name)
    {
        var offset = 0;
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            if (offset > 0 && line is "---") { break; }
            if (line.StartsWith("name:", StringComparison.Ordinal))
            { return text[..offset] + "name: " + name + text[(offset + line.Length)..]; }
            offset += line.Length;
            if (offset < text.Length && text[offset] == '\r') { offset++; }
            if (offset < text.Length && text[offset] == '\n') { offset++; }
        }
        throw new InvalidDataException("The source has no supported declared name to adapt; edit it explicitly before copying.");
    }

    private async Task Route(CustomizationCommand command)
    {
        Open();
        if (window is null) { return; }
#pragma warning disable VSTHRD003 // Await this controller's already-owned operation before accepting another original command.
        await active;
#pragma warning restore VSTHRD003
        if (busy || !Eligible()) { return; }
        pendingCommand = command;
        var operation = Run("Command");
        active = operation;
        await operation;
    }

    private async Task RouteCore(CustomizationCommand command, Func<bool> current, CancellationToken token)
    {
        if (window is null) { return; }
        if (command.Kind is { } kind) { window.Kinds.SelectedIndex = (int)kind; }
        if (command.Operation is "List") { RenderSources(); return; }
        if (command.Operation is "Refine") { window.Request.Text = command.Text; await Refine(current, token); return; }
        if (command.Operation is "Create")
        {
            window.NewName.Text = command.Name;
            window.Request.Text = command.Text;
            if (command.Name is not null && await LeaveDraft(current, token)) { await Create(window.Scope, current, token); }
            else { window.Status.Text = "Choose the new artifact ID and Kora/User destination, then Add. Describe its behavior in Ask Kora before saving."; }
            return;
        }
        var matches = sources.Where(item => item.Kind == command.Kind
            && (item.Name.Equals(command.Name, StringComparison.OrdinalIgnoreCase)
                || management.ObservedArtifact(item)?.Name.Equals(command.Name, StringComparison.OrdinalIgnoreCase) == true)).ToArray();
        if (matches.Length != 1)
        { window.Search.Text = command.Name; window.Status.Text = "Choose the exact source-qualified artifact; no unique target was selected."; return; }
        if (!await LeaveDraft(current, token)) { return; }
        await Inspect(matches[0], false, current, token);
        if (command.Operation is "Disable" or "Enable") { await ChangeUse(command.Operation is "Disable", current, token); }
    }

    private void OnUseChanged(object? sender, EventArgs args) => Dispatcher.UIThread.Post(() =>
    { if (window is not null) { RetireVoiceTarget(); RenderSources(); PublishVoiceTarget(); } });
    private void OnMainChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MainViewModel.CallPolicyRevision) && !Eligible()) { OnPrivacyClosed(sender, EventArgs.Empty); }
    }
    private void OnPrivacyClosed(object? sender, EventArgs args)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => OnPrivacyClosed(sender, args)); return; }
        lifetime?.Cancel(); closing = true; window?.CancelReview(); window?.Close(); closing = false;
    }
    private void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        RetireVoiceTarget();
        if (closing) { return; }
        args.Cancel = true;
        if (!busy) { active = CloseReviewed(); }
    }
    private async Task CloseReviewed()
    {
        if (await PrepareClose()) { closing = true; window?.Close(); closing = false; }
    }
    internal async Task<bool> PrepareClose()
    {
        if (window is null) { return true; }
        if (busy) { window.CancelReview(); return false; }
        try { return await LeaveDraft(Eligible, lifetime!.Token); }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or OperationCanceledException)
        { window?.Status.Text = "Close did not complete: " + exception.Message; return false; }
    }
    private void ClearWindow()
    {
        RetireVoiceTarget();
        var retired = lifetime; lifetime = null;
        var retiredWindow = window;
        window = null; source = null; baseline = null; draft = []; selectedFile = null; sources = []; bundled.Clear();
        retiredWindow?.ClearPrivateText();
        retired?.Cancel();
        if (retired is not null) { _ = DisposeLifetime(active, retired); }
    }
    private async Task DisposeLifetime(Task operation, CancellationTokenSource retired)
    {
#pragma warning disable VSTHRD003 // Teardown retains this controller's owned worker until it has actually terminated.
        try { await operation; }
#pragma warning restore VSTHRD003
        catch (Exception exception) when (exception is not OutOfMemoryException) { CustomizationFailed(logger, exception.GetType().Name); }
        finally { retired.Dispose(); }
    }
    public void Dispose()
    {
        disposed = true;
        main.CustomizationRequested -= Route; main.PrivacyClosureRequested -= OnPrivacyClosed; main.PropertyChanged -= OnMainChanged;
        management.UseChanged -= OnUseChanged; main.BindCustomizationShutdown(null);
        main.BindCustomizationVoice(null);
        catalogue.Changed -= OnUseChanged;
        OnPrivacyClosed(this, EventArgs.Empty);
    }

    private static void RequireCurrent(Func<bool> current, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!current()) { throw new InvalidOperationException("The exact private editor was retired; late content was not published."); }
    }
}
