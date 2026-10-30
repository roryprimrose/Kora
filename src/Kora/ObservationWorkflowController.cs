using System.ComponentModel;
using Avalonia.Controls;
using Kora.Application.Skills;
using Kora.Application.ViewModels;
using Kora.Core.Hosting;
using Kora.Core.Skills;
using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class ObservationWorkflowController(
    MainViewModel main, OwnedSkillSaveWorkflow save, OwnedSkillInvocationWorkflow sources, ObservationSkillWorkflow workflow,
    Func<bool> ownsHost, Func<long> ownershipRevision, ILogger<ObservationWorkflowController> logger) : IAsyncDisposable
{
    private ObservationWorkflowWindow? window;
    private OwnedSkillAuthoringAuthority? original;
    private CancellationTokenSource? lifetime;
    private ObservationWorkflowUse? use;
    private OwnedDeclarativeSkillDocument? validated;
    private Task active = Task.CompletedTask;
    private long ownerRevision;
    private long callRevision;
    private long draftRevision;
    private long invocationRevision;
    private Guid? nextTarget;
    private bool dirty;
    private bool uncertain;
    private bool closing;
    private bool disposed;
    private bool stepPending;

#pragma warning disable VSTHRD003 // The controller retains its operation until all real work joins.
    internal Task ActiveOperation => active;
#pragma warning restore VSTHRD003

    internal void Bind()
    {
        main.PrivacyClosureRequested += PrivacyClosed;
        main.PropertyChanged += MainChanged;
    }

    private bool Eligible() => !disposed && ownsHost() && ownerRevision == ownershipRevision()
        && callRevision == main.CallPolicyRevision && main.CanRevealPrivatePresentation && main.IsSkillReviewCallEligible
        && !main.IsResponseInteractionPending && original?.IsCurrent() == true && lifetime?.IsCancellationRequested == false;

    internal async Task Handle(string input, OwnedSkillAuthoringAuthority authority)
    {
        var command = ObservationWorkflowCommand.Parse(input);
        if (command.Operation is "status")
        {
            if (main.CanRevealPrivatePresentation && window is { } opened)
            { opened.Status.Text = "Observation workflow: " + (use?.State.ToString() ?? "disabled/no reviewed use") + (active.IsCompleted ? "; owned operation joined." : "; owned operation not yet joined."); }
            else { main.ReportHostInteractionFailure("No private observation workflow status is available."); }
            return;
        }
        if (command.Operation is "approve-step" or "deny-step")
        {
            if (!stepPending || !Eligible() || !authority.IsCurrent())
            { throw new InvalidOperationException("No current exact observation-step review can accept that confirmation."); }
            window!.CompleteApproval(command.Operation is "approve-step");
            return;
        }
        if (command.Operation is "cancel")
        {
            Retire();
#pragma warning disable VSTHRD003 // A cancellation request does not release or abandon native reviews/I/O.
            await active;
#pragma warning restore VSTHRD003
            if (main.CanRevealPrivatePresentation && window is { } opened)
            { opened.Status.Text = uncertain
                ? "Cancelled/retired. Owned work has joined, but publication may have occurred. Close/discard, then List/Reopen the exact version before any retry; no rollback is claimed."
                : "Cancelled/retired. Owned work has joined. No complete workflow result is claimed. Close/discard and open a fresh review."; }
            return;
        }
        if (!active.IsCompleted) { throw new InvalidOperationException("Wait for the owned operation to join, or Cancel; no overlapping workflow is admitted."); }
        active = RunReported(command, authority);
        if (command.Operation is "invoke") { return; }
#pragma warning disable VSTHRD003 // Observe this exact controller-owned operation.
        await active;
#pragma warning restore VSTHRD003
    }

    private async Task RunReported(ObservationWorkflowCommand command, OwnedSkillAuthoringAuthority authority)
    {
        try
        {
            if (command.Operation is "Invalid") { throw new InvalidOperationException(ObservationWorkflowCommand.Syntax); }
            if (command.Operation is "close")
            {
                if (window is not { } old) { return; }
                var privateCurrent = main.CaptureDefinitionObservationEligibility();
                var owner = ownershipRevision();
                bool Private() => ownsHost() && owner == ownershipRevision() && privateCurrent() && authority.IsCurrent();
                if (dirty)
                {
                    if (!Private()) { throw new InvalidOperationException("Fresh private presentation is required to discard this retired draft."); }
                    old.Show();
                    old.Activate();
                    if (!await old.Confirm("Discard unsaved workflow draft", "This volatile draft is not autosaved. Discard and close?",
                        "Discard draft and close", Private, CancellationToken.None)) { return; }
                }
                Retire();
                closing = true;
                old.Close();
                window = null;
                closing = false;
                lifetime?.Dispose();
                lifetime = null;
                original = null;
                dirty = false;
                uncertain = false;
                return;
            }
            if (window is null)
            {
                ownerRevision = ownershipRevision();
                callRevision = main.CallPolicyRevision;
                original = authority;
                lifetime = new();
                var created = new ObservationWorkflowWindow(Start, Eligible);
                window = created;
                created.DraftChanged += () => { draftRevision++; dirty = true; validated = null; InvalidateUse(); };
                created.InvocationChanged += InvalidateUse;
                created.Closing += async (_, args) =>
                {
                    if (closing) { return; }
                    args.Cancel = true;
                    if (!active.IsCompleted) { Retire(); return; }
                    await Start("close");
                };
                created.Show();
            }
            var opened = window;
            if (!Eligible() || !authority.IsCurrent()) { throw new InvalidOperationException("Original private workflow authority expired. Close/discard before starting a fresh operation."); }
            opened.Activate();
            var token = lifetime!.Token;
            switch (command.Operation)
            {
                case "editor":
                    opened.Status.Text = "Volatile draft only. Create, declare exact ordered observations, Validate and Save disabled.";
                    break;
                case "create":
                    if (dirty) { throw new InvalidOperationException("Save or explicitly Close/discard this draft before creating another."); }
                    InvalidateUse();
                    opened.IdInput.Text = command.Id ?? opened.IdInput.Text;
                    opened.VersionInput.Text = command.Version ?? opened.VersionInput.Text;
                    opened.NameInput.Text = "";
                    opened.DescriptionInput.Text = "";
                    opened.Definition.Text = "";
                    uncertain = false;
                    dirty = false;
                    opened.Status.Text = "New workflow draft. Supply metadata and complete explicit JSON steps; no action is inferred from prose.";
                    break;
                case "name": opened.NameInput.Text = command.Literal; break;
                case "description": opened.DescriptionInput.Text = command.Literal; break;
                case "definition": opened.Definition.Text = command.Literal; break;
                case "validate":
                    validated = opened.Document();
                    opened.Status.Text = "Valid closed native observation workflow. Nothing saved, enabled or invoked.";
                    break;
                case "save":
                    if (uncertain) { throw new InvalidOperationException("Save is unconfirmed; List/Reopen the exact version before any retry."); }
                    var document = validated ?? throw new InvalidOperationException("Validate this exact draft first.");
                    var draft = draftRevision;
                    var saved = await save.Save(document, original!, () => Eligible() && draft == draftRevision
                        && ReferenceEquals(validated, document), opened, token);
                    if (saved is not null && Eligible())
                    {
                        dirty = false;
                        validated = null;
                        opened.Status.Text = "Exact immutable two-file revision durably read back, disabled. List, choose the saved revision and Active target, supply the original request, then Review.";
                    }
                    break;
                case "list":
                    var records = await save.List(original!, token);
                    if (!Eligible()) { throw new InvalidOperationException("List authority expired."); }
                    opened.Saved.ItemsSource = records.Where(row => row.Document.ObservationWorkflow is not null).ToArray();
                    opened.Status.Text = "Saved observation workflows, disabled. Selecting never enables or invokes.";
                    break;
                case "reopen":
                    if (dirty) { throw new InvalidOperationException("Save or explicitly Close/discard the current draft first."); }
                    var selected = opened.Saved.SelectedItem as OwnedDeclarativeSkillSnapshot;
                    var snapshot = await save.Read(command.Id ?? selected?.Document.Id ?? "",
                        command.Version ?? selected?.Document.Version ?? "", original!, token)
                        ?? throw new InvalidDataException("The exact saved revision is absent.");
                    if (!Eligible() || snapshot.Document.ObservationWorkflow is null)
                    { throw new InvalidOperationException("The source is not a current observation workflow."); }
                    opened.IdInput.Text = snapshot.Document.Id;
                    opened.VersionInput.Text = snapshot.Document.Version;
                    opened.NameInput.Text = snapshot.Document.Name;
                    opened.DescriptionInput.Text = snapshot.Document.Description;
                    opened.Definition.Text = snapshot.Document.Guidance;
                    dirty = false;
                    uncertain = false;
                    validated = null;
                    opened.Status.Text = "Reopened exact saved source, still disabled. Existing versions cannot be overwritten.";
                    break;
                case "targets":
                case "next-targets":
                    InvalidateUse();
                    opened.Targets.ItemsSource = null;
                    var page = await sources.ReadTargetsAsync(command.Operation is "next-targets"
                        ? nextTarget ?? throw new InvalidOperationException("No next target page.") : null, original!, Eligible, token);
                    if (!Eligible()) { throw new InvalidOperationException("Target inspection authority expired."); }
                    nextTarget = page.Next;
                    opened.Targets.ItemsSource = page.Records.Where(row => row.Authority.IsActive)
                        .Select(row => new OwnedSkillConversationTarget(row)).ToArray();
                    opened.Status.Text = "Select exactly one existing Active conversation. No history is read or written.";
                    break;
                case "target":
                    opened.Targets.SelectedItem = ((opened.Targets.ItemsSource as OwnedSkillConversationTarget[]) ?? [])
                        .SingleOrDefault(row => string.Equals(row.Entry.Authority.SessionId.Value.ToString("D"), command.Literal, StringComparison.Ordinal))
                        ?? throw new InvalidOperationException("Select an exact Active target from the current displayed page.");
                    break;
                case "request": opened.Request.Text = command.Literal; break;
                case "review":
                    InvalidateUse();
                    var source = opened.Saved.SelectedItem as OwnedDeclarativeSkillSnapshot;
                    var target = (opened.Targets.SelectedItem as OwnedSkillConversationTarget)?.Entry
                        ?? throw new InvalidOperationException("Select one exact current Active target.");
                    var exact = invocationRevision;
                    use = await workflow.Review(command.Id ?? source?.Document.Id ?? "", command.Version ?? source?.Document.Version ?? "",
                        target, opened.Request.Text ?? "", original!, () => Eligible() && exact == invocationRevision, token);
                    opened.ReviewText.Text = use.SourceText + "\nOriginal request:\n" + use.Request + "\nActive target: " + target.Authority.SessionId.Value
                        + "\nHost bounds: 1-6 steps; 8192 source bytes; 4096 total input bytes; 8192 complete result bytes; 15 seconds total invocation, including step reviews. No retry, branches, loops, implicit continuation, model or external effects.";
                    opened.Status.Text = "Review the complete source, original request, target and bounds. Separately Enable, then Invoke. Each step still needs independent approval.";
                    break;
                case "enable":
                    await workflow.Enable(use ?? throw new InvalidOperationException("Review an exact saved source/request/target first."), token);
                    opened.Status.Text = "Exact reviewed workflow enabled once, temporarily. No step is approved or executed.";
                    break;
                case "invoke":
                    var reviewed = use ?? throw new InvalidOperationException("Review and separately enable this workflow first.");
                    var binding = invocationRevision;
                    var result = await workflow.Invoke(reviewed, async (step, current, stepToken) =>
                    {
                        stepPending = true;
                        try
                        {
                            return await opened.Confirm("Approve exact observation step",
                                "Step " + step.Number + ": " + step.Tool + "\nExact input:\n" + step.Input
                                + "\nOriginal request:\n" + reviewed.Request + "\nActive target: " + reviewed.Target.Authority.SessionId.Value
                                + "\nRead-only original host action only. This approval grants nothing to later steps.",
                                "Approve this step once", () => Eligible() && current(), stepToken);
                        }
                        finally { stepPending = false; }
                    }, token);
                    if (!Eligible() || binding != invocationRevision || !ReferenceEquals(use, reviewed))
                    { throw new InvalidOperationException("Presentation or exact request authority expired; the late result is suppressed."); }
                    opened.ReviewText.Text = "";
                    opened.Result.Text = result;
                    opened.Status.Text = "Succeeded. Every declared observation ran once in order and all owned work joined. Result is native-only, not retained in history or disclosed to a model.";
                    break;
                default: throw new InvalidOperationException(ObservationWorkflowCommand.Syntax);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (exception is OwnedSkillPublicationUnconfirmedException) { uncertain = true; }
            WorkflowFailed(logger, exception.GetType().Name);
            use?.Dispose();
            window?.ClearReview();
            if (main.CanRevealPrivatePresentation && window is { } opened)
            { opened.Status.Text = exception is OperationCanceledException
                ? "Cancelled. Owned work joined; no complete result is published and no remaining step was dispatched."
                : "Observation workflow failed: " + exception.Message + " No complete result or rollback is claimed. Review again explicitly."; }
            main.ReportHostInteractionFailure("Observation workflow unavailable, cancelled or failed. No complete result is claimed.");
        }
    }

    private async Task Start(string action)
    {
        try { await Handle(action, new(RequestOrigin.LocalUi, Eligible)); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { WorkflowFailed(logger, exception.GetType().Name); main.ReportHostInteractionFailure(exception.Message); }
    }

    private void InvalidateUse()
    {
        invocationRevision++;
        use?.Dispose();
        use = null;
        window?.ClearReview();
    }

    private void Retire()
    {
        original?.Retire();
        lifetime?.Cancel();
        use?.Cancel();
        window?.ClearReview();
    }

    private void PrivacyClosed(object? sender, EventArgs args) { Retire(); window?.Hide(); }
    private void MainChanged(object? sender, PropertyChangedEventArgs args) { if (!Eligible()) { Retire(); } }

    internal async Task CloseAndWait()
    {
        Retire();
#pragma warning disable VSTHRD003 // Hold the owner until actual operation termination, including cancellation-ignoring source reads.
        await active;
#pragma warning restore VSTHRD003
        use?.Dispose();
        use = null;
        closing = true;
        window?.Close();
        window = null;
        closing = false;
        lifetime?.Dispose();
        lifetime = null;
    }

    internal async Task<bool> PrepareClose()
    {
        if (!active.IsCompleted)
        {
            Retire();
#pragma warning disable VSTHRD003 // Shutdown retains the operation until actual termination.
            await active;
#pragma warning restore VSTHRD003
        }
        if (dirty && window is { } opened)
        {
            var privateCurrent = main.CaptureDefinitionObservationEligibility();
            var owner = ownershipRevision();
            bool Private() => ownsHost() && owner == ownershipRevision() && privateCurrent();
            if (!Private()) { return false; }
            opened.Show();
            if (!await opened.Confirm("Unsaved observation workflow", "The volatile workflow draft is not autosaved. Save it before closing, or explicitly discard it.",
                "Discard draft and close", Private, CancellationToken.None)) { return false; }
        }
        await CloseAndWait();
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        disposed = true;
        await CloseAndWait();
        main.PrivacyClosureRequested -= PrivacyClosed;
        main.PropertyChanged -= MainChanged;
    }
}
