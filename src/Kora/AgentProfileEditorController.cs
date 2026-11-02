using System.ComponentModel;

using Avalonia.Controls;

using Kora.Application.Agents;
using Kora.Application.Definitions;
using Kora.Application.ViewModels;
using Kora.Core.Artifacts;
using Kora.Core.Hosting;
using Kora.Core.Agents;
using Kora.Core.Diagnostics;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class AgentProfileEditorController(MainViewModel main, AgentProfileSaveWorkflow workflow,
    Func<bool> ownsHost, Func<long> ownerRevision, ILogger<AgentProfileEditorController> logger,
    CustomizationAuthoringService? authoring = null, ArtifactCatalogue? catalogue = null, TimeProvider? time = null)
{
    private AgentProfileEditorWindow? window;
    private AgentProfileSaveReview? review;
    private CancellationTokenSource? lifetime;
    private Task active = Task.CompletedTask;
    private Task privacyCleanup = Task.CompletedTask;
    private Func<bool>? originalEligible;
    private RequestOrigin originalOrigin;
    private long capturedOwner;
    private long revision;
    private string baseline = string.Empty;
    private string? draftName;
    private OwnedAgentProfileSnapshot? originalSnapshot;
    private bool uncertain;
    private bool closing;
    private bool stopped;
    private bool decidingClose;

#pragma warning disable VSTHRD003 // Exposes only the exact owned work for deterministic lifecycle observation.
    internal Task ActiveOperation => active;
#pragma warning restore VSTHRD003

    internal void Bind()
    {
        main.AgentProfileAuthoringRequested += Route;
        main.PrivacyClosureRequested += PrivacyClosed;
        main.PropertyChanged += MainChanged;
        if (catalogue is not null) { catalogue.Changed += CatalogueChanged; }
        main.BindAgentProfileAuthoringShutdown(ShutdownWithResult);
    }

    internal Task Add(string name, string description, RequestOrigin origin, Func<bool> eligible) =>
        Route(new(string.IsNullOrWhiteSpace(description) ? AgentProfileAuthoringAction.Create
            : AgentProfileAuthoringAction.DraftFromDescription, name) { Description = description }, origin, eligible);

    private bool Eligible() => !stopped && window is not null && ownsHost()
        && capturedOwner == ownerRevision() && main.CanRevealPrivatePresentation
        && main.IsSkillReviewCallEligible && !main.IsResponseInteractionPending;
    private bool Dirty => window is { Source.IsReadOnly: false }
        && !string.Equals(window.Source.Text ?? string.Empty, baseline, StringComparison.Ordinal);

    private void Open()
    {
        if (window is not null) { window.Activate(); return; }
        if (stopped || !privacyCleanup.IsCompleted || !ownsHost() || !main.CaptureInstructionEditorEligibility()())
        { throw new InvalidOperationException("The current private owning host is unavailable or still retiring authoring work."); }
        capturedOwner = ownerRevision();
        lifetime = new();
#pragma warning disable MA0147 // Native button boundary; Start joins the owned operation and reports failures.
        window = new(Start);
#pragma warning restore MA0147
        window.Source.PropertyChanged += Edited;
        window.NameInput.PropertyChanged += Edited;
        window.DescriptionRequest.PropertyChanged += DescriptionEdited;
        window.Inventory.SelectionChanged += (_, _) => Retire();
        window.Closing += Closing;
        window.Show();
    }

    private async Task Route(AgentProfileAuthoringCommand command, RequestOrigin origin, Func<bool> eligible)
    {
        try
        {
            if (!eligible()) { throw new InvalidOperationException("Original queued authoring input expired."); }
            Open();
            if (command.Action == AgentProfileAuthoringAction.CancelReview)
            { Retire(); window!.Status.Text = "Proposal and save review cancelled. No write requested."; return; }
            if (!Eligible() || !active.IsCompleted || decidingClose)
            { throw new InvalidOperationException("Wait for the current owned authoring operation."); }
            active = Reported(() => RunCommand(command, origin, eligible));
            await JoinActive();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { Report(exception); }
    }

    private async Task RunCommand(AgentProfileAuthoringCommand command, RequestOrigin origin, Func<bool> eligible)
    {
        var opened = window!;
        if (command.Action is AgentProfileAuthoringAction.Create or AgentProfileAuthoringAction.DraftFromDescription)
        {
            opened.NameInput.Text = command.Argument;
            await CreateDraft(origin, eligible);
            if (command.Action == AgentProfileAuthoringAction.DraftFromDescription
                && string.Equals(draftName, command.Argument, StringComparison.Ordinal) && !Dirty)
            {
                opened.DescriptionRequest.Text = command.Description;
                descriptionEligible = eligible;
                descriptionOrigin = origin;
                await Run("Ask", eligible);
            }
            return;
        }
        if (command.Action == AgentProfileAuthoringAction.Stage)
        {
            RequireDraft();
            if (!eligible() || originalEligible?.Invoke() != true)
            { throw new InvalidOperationException("Original source authority expired; no later input renews it."); }
            var original = originalEligible;
            originalEligible = () => original() && eligible();
            opened.Source.Text = command.Argument;
            return;
        }
        if (command.Action == AgentProfileAuthoringAction.Describe)
        {
            RequireDraft();
            opened.DescriptionRequest.Text = command.Argument;
            descriptionEligible = eligible;
            descriptionOrigin = origin;
            return;
        }
        if (command.Action == AgentProfileAuthoringAction.ReviewSave && originalSnapshot is not null)
        { throw new InvalidOperationException("Save as new cannot replace an existing original. Use native Review Save for this existing original."); }
        await Run(command.Action switch
        {
            AgentProfileAuthoringAction.Validate => "Validate",
            AgentProfileAuthoringAction.ReviewSave => "Review",
            AgentProfileAuthoringAction.List => "List",
            AgentProfileAuthoringAction.Inspect => "Inspect",
            AgentProfileAuthoringAction.CancelReview => "Cancel",
            AgentProfileAuthoringAction.Ask => "Ask",
            _ => "Open",
        }, eligible, command.Argument);
    }

    private void Edited(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == TextBox.TextProperty) { Retire(); }
    }
    private void Retire()
    {
        Interlocked.Increment(ref revision);
        proposalLifetime?.Cancel();
        Interlocked.Exchange(ref review, null)?.Dispose();
        var opened = window;
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) { opened?.RetireReview(); }
        else { Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (ReferenceEquals(window, opened)) { opened?.RetireReview(); } }); }
    }
    private void MainChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MainViewModel.CallPolicyRevision) or nameof(MainViewModel.CanRevealPrivatePresentation))
        { Retire(); }
    }
    private void CatalogueChanged(object? sender, EventArgs args) => Retire();

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "Native event boundary joins owned operations and reports all nonfatal failures.")]
    private async void Start(string action)
    {
        try
        {
            if (action is "Cancel")
            { Retire(); window!.Status.Text = "Proposal and save review cancelled. No write requested."; return; }
            if (!active.IsCompleted || decidingClose)
            { window!.Status.Text = "Wait for the current owned operation."; return; }
            var eligible = main.CaptureInstructionEditorEligibility();
            using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
            active = Reported(() => string.Equals(action, "Create", StringComparison.Ordinal)
                ? CreateDraft(RequestOrigin.LocalUi, eligible) : Run(action, eligible));
            await JoinActive();
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { Report(exception); }
    }

    private async Task CreateDraft(RequestOrigin origin, Func<bool> eligible)
    {
        var opened = window!;
        var name = OwnedInstructionNames.WriteName(opened.NameInput.Text ?? string.Empty);
        if (!await Abandon(opened) || !Eligible() || !eligible()) { return; }
        Retire();
        draftName = name;
        originalSnapshot = null;
        opened.NameInput.IsReadOnly = false;
        originalOrigin = origin;
        originalEligible = eligible;
        proposalDestination = null;
        proposalEligible = null;
        opened.DescriptionRequest.Text = string.Empty;
        uncertain = false;
        baseline = string.Empty;
        opened.Source.IsReadOnly = false;
        opened.Source.Text = string.Empty;
        opened.Status.Text = "New volatile draft. Supply the complete v1 or v4 source, validate and review Save as new. Nothing saved or selected.";
    }

    private void RequireDraft()
    {
        if (!Eligible() || window!.Source.IsReadOnly || draftName is null
            || !string.Equals(draftName, window.NameInput.Text, StringComparison.Ordinal)
            || originalEligible?.Invoke() != true || uncertain)
        { throw new InvalidOperationException("Create a new draft or explicitly edit the inspected existing original with current authority. An uncertain destination must be inspected; no retry is approved."); }
    }

    private async Task Run(string action, Func<bool> eligible, string? argument = null)
    {
        var opened = window ?? throw new InvalidOperationException("Authoring is closed.");
        var token = lifetime!.Token;
        bool Current() => Eligible() && eligible() && ReferenceEquals(window, opened);
        if (!Current()) { throw new InvalidOperationException("Private original authoring admission changed."); }
        if (action is "Open") { return; }
        if (action is "Cancel") { Retire(); opened.Status.Text = "Save review cancelled. No write requested."; return; }
        if (action is "List")
        {
            Retire();
            var names = await workflow.ListAsync(Current, token);
            if (!Current()) { return; }
            opened.Inventory.ItemsSource = names;
            opened.Inventory.SelectedIndex = -1;
            opened.Status.Text = "Owned originals only. Inspect, then explicitly Edit existing original. All remain inert and unselected for execution.";
            return;
        }
        if (action is "Inspect" or "Reload")
        {
            var name = action is "Reload" ? originalSnapshot?.Destination.Name
                : string.IsNullOrEmpty(argument) ? opened.Inventory.SelectedItem as string : argument;
            if (name is null) { throw new InvalidOperationException("Select one owned original or provide its exact owned ID."); }
            if (!await Abandon(opened) || !Current()) { return; }
            Retire();
            var captured = revision;
            var saved = await workflow.ReadAsync(name, () => Current() && revision == captured, token)
                ?? throw new InvalidOperationException("The selected owned original is missing.");
            if (!Current() || revision != captured) { return; }
            draftName = null; originalEligible = null; uncertain = false;
            originalSnapshot = saved;
            proposalDestination = null; proposalEligible = null;
            opened.NameInput.Text = name;
            opened.NameInput.IsReadOnly = true;
            opened.Source.IsReadOnly = true;
            opened.Source.Text = baseline = saved.Document.Document.Text;
            opened.Status.Text = $"Exact saved original reopened read-only: {saved.Destination.DisplayPath}. Not selected, enabled or invoked.";
            return;
        }
        if (action is "Edit")
        {
            if (originalSnapshot is null || !opened.Source.IsReadOnly || uncertain)
            { throw new InvalidOperationException("Inspect the exact saved original before choosing Edit existing original."); }
            Retire();
            draftName = originalSnapshot.Destination.Name;
            originalOrigin = RequestOrigin.LocalUi;
            originalEligible = eligible;
            opened.Source.IsReadOnly = false;
            opened.Status.Text = "Editing the same pinned existing original in memory only. Validate, Review Save, then separately confirm. No activation or invocation.";
            return;
        }
        RequireDraft();
        if (action is "Ask")
        {
            if (originalSnapshot is not null)
            { throw new InvalidOperationException("Existing-original editing is manual only. Conversational generation remains create-only."); }
            await Propose(opened, Current, token); return;
        }
        if (action is "Validate")
        {
            Retire();
            _ = workflow.Validate(draftName!, opened.Source.Text ?? string.Empty);
            opened.Status.Text = "Complete compatible owned profile validated with host-clamped budgets. No write, resolution, enablement, selection or execution.";
            return;
        }
        if (action is "Review")
        { await PrepareReview(opened, Current, token); return; }
        if (action is "Confirm") { await Commit(opened, Current, token); }
    }

    private async Task PrepareReview(AgentProfileEditorWindow opened, Func<bool> current, CancellationToken token)
    {
        Retire();
        var captured = Interlocked.Read(ref revision);
        bool Exact() => current() && originalEligible?.Invoke() == true && (proposalEligible?.Invoke() ?? true)
            && captured == Interlocked.Read(ref revision);
        var proposed = await (originalSnapshot is { } original
            ? workflow.ReviewUpdateAsync(original, opened.Source.Text ?? string.Empty, captured, originalOrigin, Exact, token)
            : workflow.ReviewAsync(draftName!, opened.Source.Text ?? string.Empty, captured, originalOrigin, Exact, token));
        if (proposalDestination is { } destination && proposed.Destination != destination)
        { proposed.Dispose(); throw new InvalidOperationException("The generated draft's reviewed destination changed. No new root or fallback is approved."); }
        if (!Exact()) { proposed.Dispose(); return; }
        review = proposed;
        opened.ShowReview(proposed);
        opened.Status.Text = "Review the complete original/proposed source and exact destination, then use the separate native confirmation. No file written.";
    }

    private async Task Commit(AgentProfileEditorWindow opened, Func<bool> current, CancellationToken token)
    {
        var pending = review ?? throw new InvalidOperationException("Review the complete exact source, base and destination first.");
        review = null;
        opened.Confirm.IsEnabled = false;
        var completeReview = opened.Review.Text ?? string.Empty;
        var updating = pending.Original is not null;
        var label = updating ? "Save existing original" : "Save as new";
        try
        {
            var result = await workflow.SaveAsync(pending, RequestOrigin.LocalUi,
                async admitted => await opened.Choose("Confirm " + label,
                    completeReview, [label, "Cancel"], admitted) == 0,
                current, token);
            if (!current()) { return; }
            if (result.State is not (AgentProfileWriteState.Saved or AgentProfileWriteState.Unchanged))
            {
                if (updating && result.State == AgentProfileWriteState.Conflict)
                { await RecoverConflict(opened, result.Snapshot, current, token); return; }
                opened.Status.Text = result.State == AgentProfileWriteState.Conflict
                    ? "Destination occupied. Choose a different new ID or cancel; no overwrite."
                    : "Save cancelled. Volatile draft retained; no write requested.";
                return;
            }
            var saved = result.Snapshot!;
            baseline = saved.Document.Document.Text;
            opened.Source.IsReadOnly = true;
            originalSnapshot = saved;
            opened.NameInput.IsReadOnly = true;
            draftName = null; originalEligible = null;
            opened.Inventory.ItemsSource = new[] { saved.Destination.Name };
            opened.Inventory.SelectedItem = null;
            opened.Status.Text = $"Saved exact durable original: {saved.Destination.DisplayPath}. Inert and unselected. Reopen read-only from owned inventory.";
        }
        catch (AgentProfilePublicationUnconfirmedException) { uncertain = true; throw; }
        finally
        {
            pending.Dispose();
            if (review is null) { opened.RetireReview(); }
        }
    }

    private async Task RecoverConflict(AgentProfileEditorWindow opened, OwnedAgentProfileSnapshot? observed,
        Func<bool> current, CancellationToken token)
    {
        opened.RetireReview();
        var captured = Interlocked.Read(ref revision);
        bool Exact() => current() && originalEligible?.Invoke() == true && captured == Interlocked.Read(ref revision);
        var choice = await opened.Choose("Existing original changed",
            "The reviewed base is changed, replaced or missing. No update was published. "
            + "Reload abandons this draft only after a successful read. Overwrite prepares a NEW complete mutation review "
            + "against the observed conflict; it does not save or approve another change.\n\n"
            + $"DESTINATION\n{originalSnapshot!.Destination.DisplayPath}\n\nOBSERVED SOURCE\n"
            + (observed?.Document.Document.Text ?? "(missing; recreation is not approved)")
            + $"\n\nRETAINED KORA DRAFT\n{opened.Source.Text}",
            ["Reload from disk (abandon draft)", "Overwrite with Kora changes", "Cancel (keep draft)"], token);
        if (!Exact()) { throw new InvalidOperationException("The conflict decision expired. Draft retained."); }
        if (choice == 0)
        { await Run("Reload", current); return; }
        if (choice == 1)
        {
            if (observed is null || observed.Destination != originalSnapshot.Destination)
            { throw new InvalidOperationException("The same existing owned original is unavailable. No recreation or new destination is approved."); }
            originalSnapshot = observed;
            await PrepareReview(opened, current, token);
            return;
        }
        opened.Status.Text = "Conflict cancelled. Volatile draft retained. No overwrite, retry or new copy.";
    }

    private async Task<bool> Abandon(AgentProfileEditorWindow opened)
    {
        if (!Dirty) { return true; }
        var choice = await opened.Choose("Unsaved agent draft", "Save, discard, or keep the current volatile draft?",
            ["Save", "Discard", "Cancel"], lifetime!.Token);
        if (choice == 1) { return true; }
        if (choice != 0) { return false; }
        RequireDraft();
        await SaveOnClose(opened, main.CaptureInstructionEditorEligibility());
        return !Dirty && !uncertain;
    }

    private void Closing(object? sender, WindowClosingEventArgs args)
    {
        if (closing) { return; }
        args.Cancel = true;
        CloseFromWindow();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "Native window event boundary; joins owned work and reports failures.")]
    private async void CloseFromWindow()
    {
        try { if (await PrepareClose()) { Clear(); } }
        catch (Exception exception) when (exception is not OutOfMemoryException) { Report(exception); }
    }

    private async Task<bool> PrepareClose()
    {
        if (decidingClose) { return false; }
        decidingClose = true;
        try
        {
            await JoinActive();
#pragma warning disable VSTHRD003 // Join the exact cleanup retained after privacy cancellation.
            await privacyCleanup;
#pragma warning restore VSTHRD003
            if (window is not { } opened || !Dirty) { return true; }
            var choice = await opened.Choose("Unsaved agent draft", "Save, discard, or cancel closing/shutdown?",
                ["Save", "Discard", "Cancel"], lifetime!.Token);
            if (choice == 1) { return true; }
            if (choice != 0) { return false; }
            var eligible = main.CaptureInstructionEditorEligibility();
            RequireDraft();
            using var activity = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Request);
            active = Reported(() => SaveOnClose(opened, eligible));
            await JoinActive();
            return !Dirty && !uncertain;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { Report(exception); return false; }
        finally { decidingClose = false; }
    }
    private async Task SaveOnClose(AgentProfileEditorWindow opened, Func<bool> eligible)
    {
        await PrepareReview(opened, eligible, lifetime!.Token);
        if (review is not null) { await Commit(opened, eligible, lifetime.Token); }
    }
    private async Task<bool> ShutdownWithResult()
    {
        if (!await PrepareClose()) { return false; }
        stopped = true;
        Clear();
        main.AgentProfileAuthoringRequested -= Route;
        main.PrivacyClosureRequested -= PrivacyClosed;
        main.PropertyChanged -= MainChanged;
        if (catalogue is not null) { catalogue.Changed -= CatalogueChanged; }
        main.BindAgentProfileAuthoringShutdown(null);
        return true;
    }

    private void PrivacyClosed(object? sender, EventArgs args)
    {
        lifetime?.Cancel();
        privacyCleanup = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(RetireAfterWork);
    }
    private async Task RetireAfterWork()
    {
        Retire();
        window?.ClearPrivateText();
        try { await JoinActive(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { Report(exception); }
        Clear();
    }
    private async Task JoinActive()
    {
#pragma warning disable VSTHRD003 // Await this controller's exact operation, including cancellation-ignoring I/O.
        await active;
#pragma warning restore VSTHRD003
    }
    private async Task Reported(Func<Task> operation)
    {
        try { await operation(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { Report(exception); }
    }
    private void Clear()
    {
        Retire();
        lifetime?.Cancel();
        closing = true;
        window?.ClearPrivateText();
        window?.Close();
        closing = false;
        window = null;
        lifetime?.Dispose(); lifetime = null;
        originalEligible = null; draftName = null; originalSnapshot = null; baseline = string.Empty;
        descriptionEligible = null; proposalDestination = null; proposalEligible = null;
    }
    private void Report(Exception exception)
    {
        AuthoringFailed(logger, exception.GetType().Name);
        var message = exception is AgentProfilePublicationUnconfirmedException
            ? "Publication occurred but completion is unconfirmed. Inspect the exact owned destination. No retry or rollback claimed."
            : "Agent authoring unavailable, cancelled or revoked. Draft retained where private. No success, retry or rollback claimed.";
        if (window is { } opened && main.CanRevealPrivatePresentation)
        {
            opened.Status.Text = message + (exception is InvalidDataException or InvalidOperationException
                ? " " + exception.Message : string.Empty);
        }
        main.ReportHostInteractionFailure(message);
    }
}
