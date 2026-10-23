using Avalonia.Controls;

using Kora.Application.Definitions;
using Kora.Application.ViewModels;
using Kora.Core.Artifacts;
using Kora.Core.Dependencies;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class OwnedInstructionEditorController(
    MainViewModel main, OwnedInstructionEditorActions actions, InstructionSaveWorkflow saves, ArtifactCatalogueReloadService reload,
    IApplicationDataPaths paths,
    Func<bool> ownsHost, Func<long> ownershipRevision, ILogger<OwnedInstructionEditorController> logger,
    OwnedInstructionEditorActions? promptActions = null, InstructionSaveWorkflow? promptSaves = null,
    ProfileInstructionForkWorkflow? profileForks = null,
    Kora.Core.Context.IUserFilePicker? profilePicker = null) : IDisposable
{
    private ArtifactKind kind = ArtifactKind.Instruction;
    private OwnedInstructionEditorActions Actions => kind == ArtifactKind.Instruction ? actions
        : promptActions ?? throw new InvalidOperationException("Prompt editing is unavailable.");
    private InstructionSaveWorkflow Saves => kind == ArtifactKind.Instruction ? saves
        : promptSaves ?? throw new InvalidOperationException("Prompt editing is unavailable.");
    private OwnedInstructionEditorWindow? window;
    private OwnedInstructionSnapshot? baseline;
    private string originalText = string.Empty;
    private string? selectedName;
    private CancellationTokenSource? lifetime;
    private bool busy;
    private bool closing;
    private bool disposed;
    private bool uncertain;
    private bool newDraft;
    private long sourceRevision;
    private long copyInputRevision;
    private long windowOwnershipRevision;

    internal void Bind()
    {
        main.OwnedInstructionEditorRequested += OnRequested;
        main.OwnedPromptEditorRequested += OnPromptRequested;
        main.PrivacyClosureRequested += OnPrivacyClosed;
        main.PropertyChanged += OnMainInputChanged;
        actions.InventoryChanged += OnInventoryChanged;
        actions.InventoryReloadFailed += OnInventoryReloadFailed;
        main.BindInstructionEditorShutdown(PrepareClose);
    }

    private void OnMainInputChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MainViewModel.CommandText))
        {
            Interlocked.Increment(ref copyInputRevision);
            var opened = window;
            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) { opened?.CancelRebaseReview(); opened?.ClearValidation(); }
            else { Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (ReferenceEquals(window, opened)) { opened?.CancelRebaseReview(); opened?.ClearValidation(); } }); }
        }
        if (args.PropertyName is nameof(MainViewModel.CallPolicyRevision))
        { RetireInventory("Call/runtime admission changed. Refresh and reselect after recovery; the open draft is retained."); }
    }

    internal void Open(bool load = true)
    {
        if (disposed) { return; }
        if (!ownsHost() || !main.CaptureInstructionEditorEligibility()())
        {
            if (window is not null) { OnPrivacyClosed(this, EventArgs.Empty); }
            main.ReportHostInteractionFailure("Owned instruction editing requires the current unlocked owning host and eligible clear call/privacy state.");
            return;
        }
        if (window is not null)
        {
            if (ownershipRevision() != windowOwnershipRevision) { OnPrivacyClosed(this, EventArgs.Empty); return; }
            window.Activate(); return;
        }
        windowOwnershipRevision = ownershipRevision();
        lifetime = new();
        window = new(action =>
        {
            if (action is "Cancel profile copy") { CancelProfileCopy(); }
            else if (action is "Filter" or "Next") { FilterInventory(action is "Next", edited: action is "Filter"); }
            else { _ = Run(action); }
        });
        var findOwner = window;
        window.BindBufferFind(() => CaptureBufferFind(findOwner));
        window.BindGoToLine(CaptureGoToLine);
        window.Source.PropertyChanged += (_, change) =>
        {
            if (change.Property == TextBox.TextProperty || change.Property == TextBox.IsReadOnlyProperty || change.Property == TextBox.IsEnabledProperty)
            { Interlocked.Increment(ref sourceRevision); window?.CancelRebaseReview(); window?.ClearValidation(); }
        };
        window.CopyNameInput.PropertyChanged += (_, change) =>
        {
            if (change.Property == TextBox.TextProperty) { Interlocked.Increment(ref sourceRevision); window?.CancelRebaseReview(); window?.RetireMetadataFind(); }
        };
        window.NameInput.PropertyChanged += (_, change) =>
        {
            if (change.Property == TextBox.TextProperty) { Interlocked.Increment(ref sourceRevision); inventoryChoiceWithdrawn = false; window?.CancelRebaseReview(); window?.ClearValidation(); }
        };
        window.Files.SelectionChanged += (_, _) =>
        {
            Interlocked.Increment(ref sourceRevision);
            window?.CancelRebaseReview();
            window?.ClearValidation();
            if (window is { } opened) { AdmitSelectedMetadata(opened); }
        };
        window.KindInput.SelectionChanged += (_, _) => { Interlocked.Increment(ref sourceRevision); window?.CancelRebaseReview(); window?.ClearValidation(); };
        window.SelectKind(kind);
        window.Closing += OnClosing;
        window.Closed += (_, _) => Clear();
        window.Show();
        Open(load: false);
        if (load) { _ = Run("List"); }
    }

    private bool Dirty => window is not null && !string.Equals(window.Source.Text ?? string.Empty, originalText, StringComparison.Ordinal);

    private async Task OnRequested(string operation, string? name)
        => await OnRequested(ArtifactKind.Instruction, operation, name);

    private async Task OnPromptRequested(string operation, string? name)
        => await OnRequested(ArtifactKind.Prompt, operation, name);

    private async Task OnRequested(ArtifactKind requestedKind, string operation, string? name)
    {
        if (operation is "Validate" or "Compare" or "Rebase")
        {
            if (window is null || lifetime is null || requestedKind != kind)
            { main.ReportHostInteractionFailure("Validate/Compare/Rebase requires the currently open same-kind owned draft. Inspect or Create explicitly; no source is opened implicitly."); return; }
            window.Activate();
            await Run(operation);
            return;
        }
        Open(load: false);
        if (window is null || lifetime is null) { return; }
        if (operation is "Cancel" && requestedKind == kind)
        {
            if (ownsHost() && main.CaptureInstructionEditorEligibility()()) { window.CancelReview(); }
            return;
        }
        if (busy) { return; }
        if (operation is "Save as new copy")
        {
            if (requestedKind != kind) { window.Report("Copy must retain the open original kind. Inspect a supported owned source first."); return; }
            window.CopyNameInput.Text = name;
        }
        else
        {
            if (!await SwitchKind(window, requestedKind, lifetime.Token)) { return; }
            if (name is not null) { window.NameInput.Text = name; window.Files.SelectedItem = null; inventoryChoiceWithdrawn = false; }
        }
        await Run(operation, name);
    }

    private async Task Run(string action, string? exactName = null)
    {
        window?.InvalidateGoToLine();
        if (busy || disposed || window is not { } opened || lifetime is null)
        {
            if (window is not null) { window.SelectKind(kind); }
            return;
        }
        var captured = main.CaptureInstructionEditorEligibility();
        var ownership = ownershipRevision();
        bool Eligible() => !disposed && ownsHost() && ownershipRevision() == ownership
            && ReferenceEquals(window, opened) && captured();
        var token = lifetime.Token;
        if (!Eligible() || ownership != windowOwnershipRevision) { OnPrivacyClosed(this, EventArgs.Empty); return; }
        opened.RetireBufferFind(clearQuery: true);
        busy = true;
        opened.RetireMetadataFind();
        try
        {
            switch (action)
            {
                case "Kind":
                    if (!await SwitchKind(opened, opened.KindInput.SelectedItem is ArtifactKind selected ? selected : kind, token))
                    { break; }
                    goto case "List";
                case "List":
                case "Refresh":
                    await RefreshInventory(opened, Eligible, token);
                    if (!Eligible() || inventory is null || !Actions.IsInventoryCurrent(inventory)) { break; }
                    opened.Report("Owned file inventory refreshed. Disabled revisions remain inspectable. Select an ID and Inspect.");
                    break;
                case "Create":
                    if (!await AbandonDraft(opened, token)) { break; }
                    var name = opened.NameInput.Text ?? string.Empty;
                    if (await Actions.Inspect(name, Eligible, token) is not null)
                    { throw new InvalidOperationException("That exact owned ID already exists. Inspect it; no implicit overwrite."); }
                    if (!Eligible()) { return; }
                    selectedName = name;
                    baseline = null;
                    newDraft = true;
                    opened.Source.IsReadOnly = false;
                    originalText = string.Empty;
                    opened.Source.Text = $"---\r\nkora-format: {(kind == ArtifactKind.Instruction ? "1" : "2")}\r\nname: {name}\r\ndescription: Describe this {kind.ToString().ToLowerInvariant()}.\r\n---\r\n"
                        + (kind == ArtifactKind.Instruction ? "Write instruction guidance here.\r\n" : "Write the literal saved user request here. Required input example: ${input:topic}\r\n");
                    opened.Report("New in-memory draft. Nothing saved or offered for use.");
                    break;
                case "Inspect":
                case "Reload":
                    await InspectChoice(opened, action is "Reload", exactName, Eligible, token);
                    break;
                case "Rebase":
                    await RebaseDraft(opened, Eligible, token);
                    break;
                case "Save":
                    await Save(opened, delete: false, Eligible, token);
                    break;
                case "Validate":
                case "Compare":
                    await ValidateDraft(opened, Eligible, token, action is "Compare");
                    break;
                case "Save as new copy":
                    await SaveCopy(opened, Eligible, token);
                    break;
                case "Create Kora copy from profile instruction":
                    await ForkProfileInstruction(opened, Eligible, token);
                    break;
                case "Delete":
                    await Save(opened, delete: true, Eligible, token);
                    break;
                case "Review use":
                    if (!opened.ReviewUse.IsEnabled || Dirty || baseline is null || uncertain || Actions.IsSourceUnavailable)
                    { throw new InvalidOperationException("Save/reload an exact clean revision before reviewing use."); }
                    var reviewed = baseline;
                    var reviewedSourceRevision = Interlocked.Read(ref sourceRevision);
                    bool ReviewCurrent() => Eligible() && ReferenceEquals(baseline, reviewed)
                        && Interlocked.Read(ref sourceRevision) == reviewedSourceRevision;
                    var activated = await Actions.Activate(reviewed, admitted => ConfirmUse(opened, reviewed, ReviewCurrent, admitted), ReviewCurrent, token);
                    if (!Eligible()) { return; }
                    opened.Report(activated ? "Exact revision reviewed for explicit use in this unlocked host run. No automatic applicability or new permissions."
                        : "Use review cancelled. No new revision applicability granted.");
                    break;
                case "Disable reviewed use":
                    await DisableUse(opened, Eligible, token);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            if (action is "Create Kora copy from profile instruction")
            {
                if (ReferenceEquals(window, opened))
                { opened.Report("Profile copy cancelled or consent withdrawn. Private review cleared; editor draft retained. A destination may already be durable but is not enabled. Inspect the exact destination before retry; no rollback or automatic retry."); }
                return;
            }
            if (action is "Rebase")
            {
                if (ReferenceEquals(window, opened))
                { opened.Report("Rebase cancelled. No source write or baseline adoption; raw draft retained. Existing publication uncertainty is unchanged."); }
                return;
            }
            if (action is "Validate" or "Compare")
            {
                if (ReferenceEquals(window, opened))
                { opened.ClearValidation(); opened.Report("Observation cancelled. Nothing saved/enabled; draft and existing source/use holds retained."); }
                return;
            }
            if (action is "Disable reviewed use")
            {
                if (ReferenceEquals(window, opened))
                { opened.Report("Disable cancelled before confirmed revocation. No source write or draft discard. Inspect current use state before another review."); }
                return;
            }
            if (action is not "Save as new copy") { uncertain = true; }
            if (ReferenceEquals(window, opened)) { opened.Report(action is "Save as new copy"
                ? "Copy cancelled. Destination may already be durable but remains held. Inspect that ID explicitly or choose another new ID; original draft retained."
                : "Cancelled. A write may already be durable; Reload/reconcile before more writes or use. Draft retained."); }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            EditorFailed(logger, exception.GetType().Name);
            if (action is "List" or "Refresh") { InvalidateInventory("Inventory unavailable; historical observations are not current. Explicit Refresh required."); }
            if (action is "Save" or "Delete"
                || exception is InstructionUseRevocationUnconfirmedException) { uncertain = true; }
            if (ReferenceEquals(window, opened))
            { opened.Report((exception is InstructionPublicationUnconfirmedException
                    ? "Atomic owned publication occurred; later completion is unconfirmed. "
                    : exception is InstructionUseRevocationUnconfirmedException revocation
                        ? $"Reviewed use is held or unavailable; completion unconfirmed. Control receipt {revocation.ControlReceipt}. Reload/read back revision preferences before fresh use review. Source and draft retained. "
                    : "Unavailable: ")
                + (exception is InvalidDataException or InvalidOperationException ? exception.Message : exception.GetType().Name)
                + ". No success or rollback claimed. Draft retained. For a copy, inspect the destination explicitly or choose another new ID; no overwrite. Profile originals are never modified."); }
        }
        finally { busy = false; }
    }

    private async Task ValidateDraft(OwnedInstructionEditorWindow opened, Func<bool> eligible, CancellationToken token, bool compare = false)
    {
        opened.ClearValidation();
        if (!opened.IsVisible || opened.Source.IsReadOnly || selectedName is not { } name || baseline is null && !newDraft
            || !string.Equals(opened.NameInput.Text, name, StringComparison.Ordinal)
            || opened.KindInput.SelectedItem is not ArtifactKind selectedKind || selectedKind != kind
            || opened.Files.SelectedItem is OwnedDefinitionInventoryRow row && !string.Equals(row.Name, name, StringComparison.Ordinal))
        { throw new InvalidOperationException("Validate/Compare requires the exact open supported owned baseline or explicitly created draft and unchanged chosen ID/kind. Inspect/Create explicitly; no file is read or created."); }
        var original = baseline;
        var draft = opened.Source.Text ?? string.Empty;
        var revision = Interlocked.Read(ref sourceRevision);
        var inputEpoch = Interlocked.Read(ref copyInputRevision);
        var epoch = Interlocked.Read(ref inventoryEpoch);
        var observedKind = kind;
        var inputCurrent = main.CaptureDefinitionObservationEligibility();
        bool Current() => eligible() && inputCurrent() && ReferenceEquals(baseline, original) && kind == observedKind
            && revision == Interlocked.Read(ref sourceRevision) && epoch == Interlocked.Read(ref inventoryEpoch)
            && inputEpoch == Interlocked.Read(ref copyInputRevision);
        OwnedDraftComparison? comparison = null;
        var observation = compare
            ? (comparison = await Actions.CompareDraft(name, original, draft, logger, Current, token)).Validation
            : await Actions.ValidateDraft(name, original, draft, logger, Current, token);
        if (!Current() || !Actions.IsDraftValidationCurrent(observation)) { return; }
        opened.ReportValidation(observation.Summary
            + (uncertain ? " Existing uncertain publication/source hold is unchanged; Reload/reconcile before Saving or use." : string.Empty));
        if (comparison is not null)
        { opened.ShowComparison(comparison, () => Current() && Actions.IsDraftValidationCurrent(observation)); }
    }

    private async Task DisableUse(OwnedInstructionEditorWindow opened, Func<bool> eligible, CancellationToken token)
    {
        if (!opened.DisableReviewedUse.IsEnabled || Dirty || baseline is not { } reviewed || uncertain
            || !string.Equals(selectedName, reviewed.Name, StringComparison.Ordinal)
            || !string.Equals(opened.NameInput.Text, reviewed.Name, StringComparison.Ordinal)
            || opened.Files.SelectedItem is OwnedDefinitionInventoryRow selection && !string.Equals(selection.Name, reviewed.Name, StringComparison.Ordinal))
        { throw new InvalidOperationException("Disable requires the exact clean inspected saved baseline. Unsaved draft retained; explicitly Save or Reload first, then review Disable separately."); }
        var sourceEpoch = Interlocked.Read(ref sourceRevision);
        var inputEpoch = Interlocked.Read(ref copyInputRevision);
        var reviewedKind = kind;
        bool CurrentReview() => eligible() && ReferenceEquals(baseline, reviewed) && kind == reviewedKind
            && Interlocked.Read(ref sourceRevision) == sourceEpoch
            && Interlocked.Read(ref copyInputRevision) == inputEpoch;
        var result = await Actions.Disable(reviewed, async admitted =>
        {
            if (!CurrentReview()) { return false; }
            var enabled = Actions.IsReviewedUseEnabled(reviewed);
            var choice = await opened.Choose($"Disable exact {reviewedKind.ToString().ToLowerInvariant()} reviewed use",
                $"Owned kind: {reviewedKind}; source: Kora-owned {OwnedDefinitionKind.DirectoryName(reviewedKind)}; file: {reviewed.CanonicalFileName}; "
                + $"digest: {reviewed.Document.Definition.Digest}; exact native source revision: {reviewed.Revision}. "
                + (enabled ? "Currently reviewed/enabled. " : "Currently disabled/held; no enablement is being invented. ")
                + "Disable explicit selection for NEW requests only. Source bytes, metadata, file permissions and the draft are unchanged. "
                + "Already captured guidance is not rewritten or rerouted; pending unadmitted choices expire. A fresh exact use review is required later.",
                reviewed.Document.Text, reviewed.Document.Text, ["Confirm disable reviewed use", "Cancel"], admitted);
            return choice == 0 && CurrentReview();
        }, CurrentReview, token);
        if (!CurrentReview()) { return; }
        opened.Report(result.Cancelled ? "Disable review cancelled. Saved source, draft and reviewed-use state unchanged."
            : $"Exact {reviewedKind} revision {reviewed.Revision}: {(result.WasEnabled ? "disabled/held" : "already disabled/held")}. "
                + $"Control receipt {result.ControlReceipt}. Source unchanged and inspectable; new selections unavailable until fresh exact review.");
    }

    private async Task<bool> ConfirmUse(OwnedInstructionEditorWindow opened, OwnedInstructionSnapshot reviewed,
        Func<bool> eligible, CancellationToken token)
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        { return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ConfirmUse(opened, reviewed, eligible, token)); }
        if (!eligible()) { return false; }
        return await opened.Choose($"Review exact {kind.ToString().ToLowerInvariant()} use",
                            $"Owned ID: {reviewed.Name}; exact digest {reviewed.Document.Definition.Digest}. "
                            + (kind == ArtifactKind.Instruction ? "This is untrusted guidance, not host policy. "
                                : "This is a saved user request, not instruction/system authority or a workflow. Native v2 required inputs need a separate complete expanded preview and fresh request admission. ")
                            + "Permit explicit selection for new requests only? "
                            + "No new permissions, executable tools, agent runtime, automatic applicability or mid-task context update.",
                            reviewed.Document.Text, reviewed.Document.Text, ["Permit explicit use", "Cancel"], token) == 0
            && eligible() && ReferenceEquals(baseline, reviewed) && !Dirty;
    }

    private async Task<bool> Save(OwnedInstructionEditorWindow opened, bool delete, Func<bool> eligible, CancellationToken token)
    {
        if (selectedName is null || uncertain || Actions.IsSourceUnavailable) { throw new InvalidOperationException("Inspect/create, or explicitly Reload/reconcile first."); }
        var name = selectedName;
        var text = opened.Source.Text ?? string.Empty;
        var reviewedSourceRevision = Interlocked.Read(ref sourceRevision);
        // Admission/publication callbacks run off the UI thread; never read an Avalonia control there.
        bool CurrentDraft() => eligible() && Interlocked.Read(ref sourceRevision) == reviewedSourceRevision;
        var result = await Saves.Save(name, baseline, text, delete,
            new NativeInstructionMutationReview(opened, Path.Combine(paths.RoamingRoot, OwnedDefinitionKind.DirectoryName(kind)), CurrentDraft, kind),
            CurrentDraft, token);
        if (!CurrentDraft()) { return false; }
        if (result.Reloaded)
        {
            baseline = result.Current; originalText = baseline?.Document.Text ?? string.Empty;
            opened.Source.Text = originalText;
            uncertain = !reload.ReloadNow();
            inventoryChoiceWithdrawn = false;
            opened.SetUseReviewAvailable(baseline is not null && !uncertain);
            opened.Report("Draft abandoned by explicit Reload. Current disk source loaded; use remains revision-reviewed.");
            return false;
        }
        if (!result.Saved) { return false; }
        if (!reload.ReloadNow()) { uncertain = true; throw new IOException("Write may be durable; catalogue publication is unconfirmed."); }
        baseline = result.Current;
        newDraft = false;
        originalText = baseline?.Document.Text ?? string.Empty;
        opened.Source.Text = originalText;
        if (result.Deleted) { selectedName = null; }
        inventoryChoiceWithdrawn = false;
        opened.SetUseReviewAvailable(baseline is not null && !uncertain);
        opened.Report(result.Unchanged ? delete ? "Reviewed original already absent; no file deleted or use enabled."
            : "Exact original bytes and identity are unchanged. Existing reviewed use preserved; no file write."
            : result.Deleted ? "Owned original deleted and catalogue reconciled."
            : "Complete bytes saved/read back and catalogue reconciled. Revision disabled until separate exact use review.");
        return true;
    }

    private async Task SaveCopy(OwnedInstructionEditorWindow opened, Func<bool> eligible, CancellationToken token)
    {
        if (baseline is not { } source || selectedName is null || opened.Source.IsReadOnly || uncertain || Actions.IsSourceUnavailable)
        { throw new InvalidOperationException("Inspect a supported original native owned source before copying. The original draft is retained."); }
        var destination = opened.CopyNameInput.Text ?? string.Empty;
        var text = opened.Source.Text ?? string.Empty;
        var sourceKind = kind;
        var revision = Interlocked.Read(ref sourceRevision);
        var inputRevision = Interlocked.Read(ref copyInputRevision);
        bool CurrentCopy() => eligible() && Interlocked.Read(ref sourceRevision) == revision
            && Interlocked.Read(ref copyInputRevision) == inputRevision
            && ReferenceEquals(baseline, source) && kind == sourceKind;
        var result = await Saves.SaveCopy(destination, source, text,
            new NativeInstructionMutationReview(opened, Path.Combine(paths.RoamingRoot, OwnedDefinitionKind.DirectoryName(sourceKind)),
                CurrentCopy, sourceKind), CurrentCopy, token, reload.ReconcileSavedCopy);
        if (!CurrentCopy()) { return; }
        if (!result.Published)
        { opened.Report("Copy review cancelled. Original source and open draft unchanged; no new file created."); return; }
        opened.Report($"New disabled {sourceKind} copy {destination}; exact digest {result.Current!.Document.Definition.Digest}. "
            + "Original source and open draft retained. List and explicitly Inspect the new ID, then independently Review use; no references retargeted.");
    }

    private async Task Load(OwnedInstructionEditorWindow opened, string? name, Func<bool> eligible, CancellationToken token,
        OwnedDefinitionInventory? observed = null, OwnedDefinitionInventoryRow? row = null)
    {
        if (name is null) { throw new InvalidOperationException("Select the exact owned instruction ID."); }
        var loaded = observed is not null && row is not null
            ? await Actions.InspectInventory(observed, row, eligible, token)
            : await Actions.Inspect(name, eligible, token);
        if (!eligible()) { return; }
        if (observed is not null && !Actions.IsInventoryCurrent(observed))
        { throw new InvalidOperationException("The observed catalogue changed before inspection delivery. Refresh and reselect; draft retained."); }
        baseline = loaded; selectedName = loaded?.Name ?? name; originalText = loaded?.Document.Text ?? string.Empty;
        newDraft = false;
        opened.NameInput.Text = selectedName;
        opened.Source.Text = originalText;
        opened.Source.IsReadOnly = false;
        if (loaded is not null)
        {
            try
            {
                OwnedInstructionNames.WriteName(loaded.Name);
                if (!string.Equals(loaded.CanonicalFileName, loaded.Name + OwnedDefinitionKind.Extension(kind), StringComparison.Ordinal))
                { throw new InvalidDataException("This original filename is read-only; external editing is required, rename/fork is not implemented."); }
                loaded.Document.RequireEditableInstruction();
            }
            catch (InvalidDataException exception)
            {
                opened.Source.IsReadOnly = true;
                opened.Report("Exact original inspected, read-only: " + exception.Message);
                uncertain = !reload.ReloadNow();
                return;
            }
        }
        uncertain = !reload.ReloadNow();
        inventoryChoiceWithdrawn = false;
        opened.SetUseReviewAvailable(loaded is not null && !uncertain);
        opened.Report(loaded is null ? "The owned original is missing; no file was written."
            : $"Owned kind {kind}; ID {name}; exact source revision {loaded.Revision}; digest {loaded.Document.Definition.Digest}; BOM={loaded.Document.HasBom}. "
                + (uncertain ? "Use state unavailable; explicit Reload/recovery required."
                    : Actions.IsReviewedUseEnabled(loaded) ? "Currently reviewed/enabled for new explicit selections."
                    : "Disabled/held; fresh exact Review use required. Source remains inspectable/editable."));
    }

    private async Task<bool> AbandonDraft(OwnedInstructionEditorWindow opened, CancellationToken token) =>
        !Dirty || await opened.Choose("Discard current draft?", "Reload/inspect/create will abandon unsaved changes.",
            originalText, opened.Source.Text, ["Discard draft and continue", "Cancel"], token) == 0;

    private async Task<bool> SwitchKind(OwnedInstructionEditorWindow opened, ArtifactKind requestedKind, CancellationToken token)
    {
        _ = OwnedDefinitionKind.DirectoryName(requestedKind);
        if (kind == requestedKind) { return true; }
        if (!await AbandonDraft(opened, token))
        { opened.SelectKind(kind); return false; }
        token.ThrowIfCancellationRequested();
        kind = requestedKind;
        InvalidateInventory("Kind changed; explicit List/Refresh required.");
        baseline = null; selectedName = null; originalText = string.Empty; uncertain = false;
        newDraft = false;
        opened.Source.Text = string.Empty; opened.NameInput.Text = string.Empty; opened.CopyNameInput.Text = string.Empty; opened.ClearInventory();
        opened.ClearSearch();
        opened.SelectKind(kind);
        return true;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (!closing && rebaseLifetime is not null)
        {
            args.Cancel = true;
            rebaseLifetime.Cancel();
            window?.CancelRebaseReview();
            return;
        }
        if (closing || !Dirty && !busy) { return; }
        args.Cancel = true;
        _ = CloseSafely();
    }

    private async Task CloseSafely()
    {
        if (await PrepareClose()) { closing = true; window?.Close(); closing = false; }
    }

    private async Task<bool> PrepareClose()
    {
        if (busy) { window?.Report("Wait for current operation to retire before closing."); return false; }
        if (window is not { } opened || lifetime is null) { return true; }
        if (!Dirty) { return true; }
        try
        {
            var choice = await opened.Choose("Unsaved instruction draft", "Save, discard, or cancel closing/shutdown?",
                originalText, opened.Source.Text, ["Save", "Discard", "Cancel"], lifetime.Token);
            if (choice == 1) { originalText = opened.Source.Text ?? string.Empty; return true; }
            if (choice == 0) { await Run("Save"); return !Dirty && !uncertain; }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { EditorFailed(logger, exception.GetType().Name); }
        return false;
    }

    private void OnPrivacyClosed(object? sender, EventArgs args) { CancelProfileCopy(); lifetime?.Cancel(); window?.ClearPrivateText(); closing = true; window?.Close(); closing = false; }
    private void Clear()
    {
        lifetime?.Cancel(); lifetime?.Dispose(); lifetime = null;
        inventory = null; inventoryPage = null; inventoryEligibility = null; inventoryChoiceWithdrawn = false;
        window?.ClearPrivateText(); window = null; baseline = null; selectedName = null; originalText = string.Empty; uncertain = false;
        newDraft = false;
    }

    public void Dispose()
    {
        disposed = true;
        main.OwnedInstructionEditorRequested -= OnRequested;
        main.OwnedPromptEditorRequested -= OnPromptRequested;
        main.PrivacyClosureRequested -= OnPrivacyClosed;
        main.PropertyChanged -= OnMainInputChanged;
        actions.InventoryChanged -= OnInventoryChanged;
        actions.InventoryReloadFailed -= OnInventoryReloadFailed;
        main.BindInstructionEditorShutdown(null);
        OnPrivacyClosed(this, EventArgs.Empty);
        Clear();
    }
}
