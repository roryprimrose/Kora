using System.ComponentModel;

using Avalonia.Controls;

using Kora.Application.Agents;
using Kora.Application.ViewModels;
using Kora.Core.Artifacts;
using Kora.Core.Hosting;
using Kora.Core.Agents;
using Kora.Core.Diagnostics;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class AgentProfileEditorController(MainViewModel main, AgentProfileSaveWorkflow workflow,
    Func<bool> ownsHost, Func<long> ownerRevision, ILogger<AgentProfileEditorController> logger)
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
        main.BindAgentProfileAuthoringShutdown(ShutdownWithResult);
    }

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
        if (command.Action == AgentProfileAuthoringAction.Create)
        {
            opened.NameInput.Text = command.Argument;
            await CreateDraft(origin, eligible);
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
        await Run(command.Action switch
        {
            AgentProfileAuthoringAction.Validate => "Validate",
            AgentProfileAuthoringAction.ReviewSave => "Review",
            AgentProfileAuthoringAction.List => "List",
            AgentProfileAuthoringAction.Inspect => "Inspect",
            AgentProfileAuthoringAction.CancelReview => "Cancel",
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

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "Native event boundary joins owned operations and reports all nonfatal failures.")]
    private async void Start(string action)
    {
        try
        {
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
        originalOrigin = origin;
        originalEligible = eligible;
        uncertain = false;
        baseline = string.Empty;
        opened.Source.IsReadOnly = false;
        opened.Source.Text = string.Empty;
        opened.Status.Text = "New volatile draft. Supply the complete v1 source, validate and review Save as new. Nothing saved or selected.";
    }

    private void RequireDraft()
    {
        if (!Eligible() || window!.Source.IsReadOnly || draftName is null
            || !string.Equals(draftName, window.NameInput.Text, StringComparison.Ordinal)
            || originalEligible?.Invoke() != true || uncertain)
        { throw new InvalidOperationException("Create an exact new draft with current original authority. An uncertain destination must be inspected; no retry is approved."); }
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
            opened.Status.Text = "Owned originals only. All remain inert and unselected for execution; reopen is read-only.";
            return;
        }
        if (action is "Inspect")
        {
            var name = string.IsNullOrEmpty(argument) ? opened.Inventory.SelectedItem as string : argument;
            if (name is null) { throw new InvalidOperationException("Select one owned original or provide its exact owned ID."); }
            if (!await Abandon(opened) || !Current()) { return; }
            Retire();
            var captured = revision;
            var saved = await workflow.ReadAsync(name, () => Current() && revision == captured, token)
                ?? throw new InvalidOperationException("The selected owned original is missing.");
            if (!Current() || revision != captured) { return; }
            draftName = null; originalEligible = null; uncertain = false;
            opened.NameInput.Text = name;
            opened.Source.IsReadOnly = true;
            opened.Source.Text = baseline = saved.Document.Document.Text;
            opened.Status.Text = $"Exact saved original reopened read-only: {saved.Destination.DisplayPath}. Not selected, enabled or invoked.";
            return;
        }
        RequireDraft();
        if (action is "Validate")
        {
            Retire();
            _ = workflow.Validate(draftName!, opened.Source.Text ?? string.Empty);
            opened.Status.Text = "Complete tool-free reference-free v1 validated with host-clamped budgets. No write, selection or execution.";
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
        bool Exact() => current() && originalEligible?.Invoke() == true && captured == Interlocked.Read(ref revision);
        var proposed = await workflow.ReviewAsync(draftName!, opened.Source.Text ?? string.Empty,
            captured, originalOrigin, Exact, token);
        if (!Exact()) { proposed.Dispose(); return; }
        review = proposed;
        opened.ShowReview(proposed);
        opened.Status.Text = "Review the entire exact source and new destination, then use the separate native confirmation. No file written.";
    }

    private async Task Commit(AgentProfileEditorWindow opened, Func<bool> current, CancellationToken token)
    {
        var pending = review ?? throw new InvalidOperationException("Review the complete exact source and new destination first.");
        review = null;
        opened.Confirm.IsEnabled = false;
        var completeReview = opened.Review.Text ?? string.Empty;
        try
        {
            var result = await workflow.SaveAsNewAsync(pending, RequestOrigin.LocalUi,
                async admitted => await opened.Choose("Confirm Save as new",
                    completeReview, ["Save as new", "Cancel"], admitted) == 0,
                current, token);
            if (!current()) { return; }
            if (result.State != AgentProfileWriteState.Saved)
            {
                opened.Status.Text = result.State == AgentProfileWriteState.Conflict
                    ? "Destination occupied. Choose a different new ID or cancel; no overwrite."
                    : "Save cancelled. No new profile created.";
                return;
            }
            var saved = result.Snapshot!;
            baseline = saved.Document.Document.Text;
            opened.Source.IsReadOnly = true;
            draftName = null; originalEligible = null;
            opened.Inventory.ItemsSource = new[] { saved.Destination.Name };
            opened.Inventory.SelectedItem = null;
            opened.Status.Text = $"Saved exact durable original: {saved.Destination.DisplayPath}. Inert and unselected. Reopen read-only from owned inventory.";
        }
        catch (AgentProfilePublicationUnconfirmedException) { uncertain = true; throw; }
        finally { pending.Dispose(); opened.RetireReview(); }
    }

    private async Task<bool> Abandon(AgentProfileEditorWindow opened) =>
        !Dirty || await opened.Choose("Unsaved agent draft", "Discard the current unsaved volatile draft?",
            ["Discard", "Cancel"], lifetime!.Token) == 0;

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
        originalEligible = null; draftName = null; baseline = string.Empty;
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
