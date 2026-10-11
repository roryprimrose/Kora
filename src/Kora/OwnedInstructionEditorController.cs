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
    Func<bool> ownsHost, Func<long> ownershipRevision, ILogger<OwnedInstructionEditorController> logger) : IDisposable
{
    private OwnedInstructionEditorWindow? window;
    private OwnedInstructionSnapshot? baseline;
    private string originalText = string.Empty;
    private string? selectedName;
    private CancellationTokenSource? lifetime;
    private bool busy;
    private bool closing;
    private bool disposed;
    private bool uncertain;

    internal void Bind()
    {
        main.OwnedInstructionEditorRequested += OnRequested;
        main.PrivacyClosureRequested += OnPrivacyClosed;
        main.BindInstructionEditorShutdown(PrepareClose);
    }

    internal void Open(bool load = true)
    {
        if (disposed) { return; }
        if (!ownsHost() || !main.CaptureInstructionEditorEligibility()())
        { main.ReportHostInteractionFailure("Owned instruction editing requires the current unlocked owning host and eligible clear call/privacy state."); return; }
        if (window is not null) { window.Activate(); return; }
        lifetime = new();
        window = new(action => _ = Run(action));
        window.Closing += OnClosing;
        window.Closed += (_, _) => Clear();
        window.Show();
        window.Activate();
        if (load) { _ = Run("List"); }
    }

    private bool Dirty => window is not null && !string.Equals(window.Source.Text ?? string.Empty, originalText, StringComparison.Ordinal);

    private async Task OnRequested(string operation, string? name)
    {
        Open(load: false);
        if (window is null) { return; }
        if (operation is "Cancel")
        {
            if (ownsHost() && main.CaptureInstructionEditorEligibility()()) { window.CancelReview(); }
            return;
        }
        if (name is not null) { window.NameInput.Text = name; window.Files.SelectedItem = name; }
        await Run(operation);
    }

    private async Task Run(string action)
    {
        if (busy || disposed || window is not { } opened || lifetime is null) { return; }
        var captured = main.CaptureInstructionEditorEligibility();
        var ownership = ownershipRevision();
        bool Eligible() => !disposed && ownsHost() && ownershipRevision() == ownership
            && ReferenceEquals(window, opened) && captured();
        var token = lifetime.Token;
        if (!Eligible()) { return; }
        busy = true;
        try
        {
            switch (action)
            {
                case "List":
                    var files = await actions.List(Eligible, token);
                    if (!Eligible()) { return; }
                    opened.Files.ItemsSource = files;
                    if (!reload.ReloadNow()) { throw new IOException("The catalogue could not be reconciled."); }
                    opened.Report("Owned file inventory refreshed. Disabled revisions remain inspectable. Select an ID and Inspect.");
                    break;
                case "Create":
                    if (!await AbandonDraft(opened, token)) { break; }
                    var name = opened.NameInput.Text ?? string.Empty;
                    if (await actions.Inspect(name, Eligible, token) is not null)
                    { throw new InvalidOperationException("That exact owned ID already exists. Inspect it; no implicit overwrite."); }
                    if (!Eligible()) { return; }
                    selectedName = name;
                    baseline = null;
                    opened.Source.IsReadOnly = false;
                    originalText = string.Empty;
                    opened.Source.Text = $"---\r\nkora-format: 1\r\nname: {name}\r\ndescription: Describe this instruction.\r\n---\r\nWrite instruction guidance here.\r\n";
                    opened.Report("New in-memory draft. Nothing saved or offered for use.");
                    break;
                case "Inspect":
                case "Reload":
                    if (!await AbandonDraft(opened, token)) { break; }
                    await Load(opened, opened.Files.SelectedItem as string ?? opened.NameInput.Text, Eligible, token);
                    break;
                case "Save":
                    await Save(opened, delete: false, Eligible, token);
                    break;
                case "Delete":
                    await Save(opened, delete: true, Eligible, token);
                    break;
                case "Review use":
                    if (Dirty || baseline is null || uncertain)
                    { throw new InvalidOperationException("Save/reload an exact clean revision before reviewing use."); }
                    var reviewed = baseline;
                    var activated = await actions.Activate(reviewed, admitted => ConfirmUse(opened, reviewed, Eligible, admitted), Eligible, token);
                    if (!Eligible()) { return; }
                    opened.Report(activated ? "Exact revision reviewed for explicit use in this unlocked host run. No automatic applicability or new permissions."
                        : "Use review cancelled. No new revision applicability granted.");
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            uncertain = true;
            if (ReferenceEquals(window, opened)) { opened.Report("Cancelled. A write may already be durable; Reload/reconcile before more writes or use. Draft retained."); }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            EditorFailed(logger, exception.GetType().Name);
            if (action is "Save" or "Delete") { uncertain = true; }
            if (ReferenceEquals(window, opened))
            { opened.Report((exception is InstructionPublicationUnconfirmedException
                    ? "Atomic instruction publication occurred; later completion is unconfirmed. "
                    : "Unavailable: ")
                + (exception is InvalidDataException or InvalidOperationException ? exception.Message : exception.GetType().Name)
                + ". No success or rollback claimed. Keep draft; Reload/reconcile when ready. Profile originals/fork remain unavailable."); }
        }
        finally { busy = false; }
    }

    private async Task<bool> ConfirmUse(OwnedInstructionEditorWindow opened, OwnedInstructionSnapshot reviewed,
        Func<bool> eligible, CancellationToken token)
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        { return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ConfirmUse(opened, reviewed, eligible, token)); }
        if (!eligible()) { return false; }
        return await opened.Choose("Review exact instruction use",
                            $"Owned ID: {reviewed.Name}; exact digest {reviewed.Document.Definition.Digest}. "
                            + "This is untrusted guidance, not host policy. Permit explicit selection for new requests only? "
                            + "No new permissions, executable tools, agent runtime, automatic applicability or mid-task context update.",
                            reviewed.Document.Text, reviewed.Document.Text, ["Permit explicit use", "Cancel"], token) == 0
            && eligible() && ReferenceEquals(baseline, reviewed) && !Dirty;
    }

    private async Task<bool> Save(OwnedInstructionEditorWindow opened, bool delete, Func<bool> eligible, CancellationToken token)
    {
        if (selectedName is null || uncertain) { throw new InvalidOperationException("Inspect/create, or explicitly Reload/reconcile first."); }
        var name = selectedName;
        var text = opened.Source.Text ?? string.Empty;
        var result = await saves.Save(name, baseline, text, delete,
            new NativeInstructionMutationReview(opened, Path.Combine(paths.RoamingRoot, "Instructions"), () => eligible()
                && string.Equals(text, opened.Source.Text ?? string.Empty, StringComparison.Ordinal)),
            eligible, token);
        if (!eligible()) { return false; }
        if (result.Reloaded)
        {
            baseline = result.Current; originalText = baseline?.Document.Text ?? string.Empty;
            opened.Source.Text = originalText;
            uncertain = !reload.ReloadNow();
            opened.Report("Draft abandoned by explicit Reload. Current disk source loaded; use remains revision-reviewed.");
            return false;
        }
        if (!result.Saved) { return false; }
        if (!reload.ReloadNow()) { uncertain = true; throw new IOException("Write may be durable; catalogue publication is unconfirmed."); }
        baseline = result.Current;
        originalText = baseline?.Document.Text ?? string.Empty;
        opened.Source.Text = originalText;
        if (result.Deleted) { selectedName = null; }
        opened.Report(result.Unchanged ? delete ? "Reviewed original already absent; no file deleted or use enabled."
            : "Exact original bytes and identity are unchanged. Existing reviewed use preserved; no file write."
            : result.Deleted ? "Owned original deleted and catalogue reconciled."
            : "Complete bytes saved/read back and catalogue reconciled. Revision disabled until separate exact use review.");
        return true;
    }

    private async Task Load(OwnedInstructionEditorWindow opened, string? name, Func<bool> eligible, CancellationToken token)
    {
        if (name is null) { throw new InvalidOperationException("Select the exact owned instruction ID."); }
        var loaded = await actions.Inspect(name, eligible, token);
        if (!eligible()) { return; }
        baseline = loaded; selectedName = loaded?.Name ?? name; originalText = loaded?.Document.Text ?? string.Empty;
        opened.Source.Text = originalText;
        opened.Source.IsReadOnly = false;
        if (loaded is not null)
        {
            try
            {
                OwnedInstructionNames.WriteName(loaded.Name);
                if (!string.Equals(loaded.CanonicalFileName, loaded.Name + ".instructions.md", StringComparison.Ordinal))
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
        opened.Report(loaded is null ? "The owned original is missing; no file was written."
            : $"Owned ID {name}; original digest {loaded.Document.Definition.Digest}; BOM={loaded.Document.HasBom}. "
                + "Exact original source loaded. Save and use review are separate.");
    }

    private async Task<bool> AbandonDraft(OwnedInstructionEditorWindow opened, CancellationToken token) =>
        !Dirty || await opened.Choose("Discard current draft?", "Reload/inspect/create will abandon unsaved changes.",
            originalText, opened.Source.Text, ["Discard draft and continue", "Cancel"], token) == 0;

    private void OnClosing(object? sender, WindowClosingEventArgs args)
    {
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

    private void OnPrivacyClosed(object? sender, EventArgs args) { lifetime?.Cancel(); window?.ClearPrivateText(); closing = true; window?.Close(); closing = false; }
    private void Clear()
    {
        lifetime?.Cancel(); lifetime?.Dispose(); lifetime = null;
        window?.ClearPrivateText(); window = null; baseline = null; selectedName = null; originalText = string.Empty; uncertain = false;
    }

    public void Dispose()
    {
        disposed = true;
        main.OwnedInstructionEditorRequested -= OnRequested;
        main.PrivacyClosureRequested -= OnPrivacyClosed;
        main.BindInstructionEditorShutdown(null);
        OnPrivacyClosed(this, EventArgs.Empty);
        Clear();
    }
}
