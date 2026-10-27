using System.ComponentModel;
using System.Diagnostics;
using Kora.Application.Skills;
using Kora.Application.ViewModels;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class NamedSkillInvocationController(
    MainViewModel main, NamedSkillCatalogue catalogue, OwnedSkillInvocationWorkflow owned,
    SharedSkillInvocationWorkflow shared, Func<bool> ownsHost, Func<long> ownershipRevision,
    ILogger<NamedSkillInvocationController> logger) : IAsyncDisposable
{
    private NamedSkillInvocationWindow? window;
    private NamedSkillCandidates? candidates;
    private NamedSkillChoice? choice;
    private OwnedSkillAuthoringAuthority? original;
    private OwnedSkillUseReview? ownedReview;
    private SharedSkillUseReview? sharedReview;
    private OwnedSkillInvocation? ownedInvocation;
    private SharedSkillInvocation? sharedInvocation;
    private CancellationTokenSource? lifetime;
    private Task active = Task.CompletedTask;
    private long owner;
    private long call;
    private Guid? next;
    private string name = "";
    private bool disposed;

#pragma warning disable VSTHRD003 // The controller retains actual I/O/model work through cancellation and physical completion.
    internal Task ActiveOperation => active;
#pragma warning restore VSTHRD003

    internal void Bind()
    {
        main.NamedSkillInvocationRequested += Route;
        main.BindNamedSkillUseShutdown(CloseAndWait);
        main.BindNamedSkillChoiceCapture(() =>
        {
            var captured = candidates;
            var capturedWindow = window;
            return () => captured is not null && ReferenceEquals(candidates, captured)
                && ReferenceEquals(window, capturedWindow) && captured.IsCurrent && Eligible();
        });
        main.PrivacyClosureRequested += OnPrivacyClosed;
        main.PropertyChanged += OnMainChanged;
    }

    private bool Eligible() => !disposed && ownsHost() && owner == ownershipRevision()
        && call == main.CallPolicyRevision && main.CanRevealPrivatePresentation && main.IsSkillReviewCallEligible
        && !main.IsResponseInteractionPending && original?.IsCurrent() == true && lifetime?.IsCancellationRequested == false;

    private async Task Route(NamedSkillCommand command, RequestOrigin origin, Func<bool> eligible)
    {
        if (!eligible()) { main.ReportHostInteractionFailure("Original named skill input expired. No fallback was run."); return; }
        if (command.Action is "Close") { await CloseAndWait(); return; }
        if (command.Action is "Review")
        {
            await CloseAndWait();
            owner = ownershipRevision();
            call = main.CallPolicyRevision;
            lifetime = new();
            original = new(origin, eligible);
            name = command.Name;
            if (!Eligible()) { Retire(); return; }
            window = new(name, Start);
            window.Closed += (_, _) => Retire();
            window.Choices.SelectionChanged += (_, _) => RetireReview();
            window.Request.PropertyChanged += (_, args) =>
            { if (args.Property == Avalonia.Controls.TextBox.TextProperty) { RetirePreparation(); } };
            window.Targets.SelectionChanged += (_, _) => RetirePreparation();
            window.Show();
            if (!Eligible()) { Retire(); return; }
            window.Status.Text = "No files were read. Use the separate native read-consent control to inspect current candidates.";
            return;
        }
        if (command.Action is "Choice" && window is { } opened && Eligible() && active.IsCompleted)
        {
            var rows = candidates?.Choices;
            if (rows is null || command.Choice > rows.Count)
            { opened.Status.Text = "Inspect first, then choose exactly one displayed numbered source. No name was guessed."; return; }
            opened.Choices.SelectedItem = rows[command.Choice - 1];
            active = RunReported("ReviewChoice");
            await active;
            return;
        }
        main.ReportHostInteractionFailure("Named skill choice is unavailable or busy. Inspect and review again; no fallback was run.");
    }

    private async Task Start(string action)
    {
        if (action is "Close")
        {
            try { await CloseAndWait(); }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            { UseFailed(logger, exception.GetType().Name); main.ReportHostInteractionFailure("Named skill shutdown is unconfirmed; resource release is not claimed."); }
            return;
        }
        if (!active.IsCompleted || !Eligible()) { return; }
        active = RunReported(action);
        await active;
    }

    private async Task RunReported(string action)
    {
        var opened = window!;
        opened.SetBusy(true);
        using var activity = HostActivity.BeginRoot(HostRequest.Create(action is "Inspect" or "Invoke" ? RequestOrigin.LocalUi : original!.Origin),
            HostActivityLayer.Desktop, HostOperation.Request,
            original!.OriginalActivityContext is { } cause ? [new ActivityLink(cause)] : []);
        try
        {
            await Run(action, opened);
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            activity.Complete(exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
            UseFailed(logger, exception.GetType().Name);
            candidates?.Dispose();
            candidates = null;
            RetireReview();
            if (ReferenceEquals(window, opened) && main.CanRevealPrivatePresentation)
            {
                opened.Choices.ItemsSource = null;
                opened.Status.Text = "Lookup or exact use unavailable, expired, changed, cancelled or unconfirmed. "
                    + "No successful empty lookup, fallback or replay is claimed. Inspect again; check the exact conversation before retrying use.";
            }
        }
        finally { if (ReferenceEquals(window, opened)) { opened.SetBusy(false); } }
    }

    private async Task Run(string action, NamedSkillInvocationWindow opened)
    {
        if (!Eligible()) { throw new InvalidOperationException("Original named skill authority expired."); }
        var token = lifetime!.Token;
        if (action is "Inspect")
        {
            candidates?.Dispose();
            RetireReview();
            opened.Choices.ItemsSource = null;
            candidates = await catalogue.InspectAsync(name, original!, Eligible, token);
            if (!Eligible()) { candidates.Dispose(); return; }
            opened.Choices.ItemsSource = candidates.Choices;
            opened.Choices.SelectedIndex = -1;
            opened.Status.Text = candidates.Choices.Count == 0
                ? "No exact declared readable name matches in the inspected saved/registered sources. Names are case-sensitive, with no Unicode normalization. Nothing invoked."
                : "Choose one numbered source explicitly, then review it. Duplicates and incompatible kinds never shadow another source; nothing enabled.";
            return;
        }
        if (action is "ReviewChoice")
        {
            var selected = opened.Choices.SelectedItem as NamedSkillChoice
                ?? throw new InvalidOperationException("Choose a displayed exact source first.");
            choice = candidates!.Choose(selected.Number);
            if (choice.Owned is not null)
            { ownedReview = await choice.ReviewOwnedAsync(owned, token); }
            else
            { sharedReview = await shared.ReviewAsync(choice.Source!, choice.Shared!, Current, original!.Origin, token); }
            if (!Current()) { RetireReview(); return; }
            opened.Instructions.Text = choice + "\n\n" + (ownedReview?.Text ?? sharedReview!.Text);
            opened.Status.Text = "Complete exact source captured. Review all content, then separately enable once. The original input channel is preserved.";
            return;
        }
        if (!Current() || ownedReview is null && sharedReview is null)
        { throw new InvalidOperationException("Review the exact current source choice first."); }
        if (action is "Enable")
        {
            if (ownedReview is { } own) { await owned.EnableAsync(own, original!, Current, token); }
            else { await shared.EnableAsync(sharedReview!, Current, original!.Origin, token); }
            if (Current()) { opened.Status.Text = "Exact revision enabled once. Select an existing Active target and enter the unchanged original task."; }
        }
        else if (action is "Targets" or "NextTargets")
        {
            var after = action is "NextTargets" ? next ?? throw new InvalidOperationException("No next page; refresh targets.") : (Guid?)null;
            RetirePreparation();
            opened.Targets.ItemsSource = null;
            var page = ownedReview is not null
                ? await owned.ReadTargetsAsync(after, original!, Current, token)
                : await shared.ReadTargetsAsync(after, Current, original!.Origin, token);
            if (!Current()) { return; }
            next = page.Next;
            opened.Targets.ItemsSource = page.Records.Where(row => row.Authority.IsActive)
                .Select(row => new SharedSkillConversationTarget(row)).ToArray();
            opened.Targets.SelectedIndex = -1;
            opened.Status.Text = "Explicitly select one displayed current Active conversation. No session was created or resumed.";
        }
        else if (action is "Prepare")
        {
            RetirePreparation();
            var target = (opened.Targets.SelectedItem as SharedSkillConversationTarget)?.Entry
                ?? throw new InvalidOperationException("Select exactly one current Active conversation.");
            var body = opened.Request.Text ?? "";
            if (ownedReview is { } own)
            {
                ownedInvocation = await owned.PrepareAsync(own, target, body, original!, Current, token);
                if (Current()) { opened.Confirmation.Text = ownedInvocation.Preview; }
            }
            else
            {
                sharedInvocation = await shared.PrepareAsync(sharedReview!, target, body, Current, original!.Origin, token);
                if (Current())
                { opened.Confirmation.Text = $"{opened.Instructions.Text}\n\nExact Active target: {target.Authority.SessionId.Value:D}\nOriginal USER task:\n{sharedInvocation.Body}"; }
            }
            if (!Current()) { RetirePreparation(); return; }
            opened.Status.Text = "Review the exact source, original task, target and local-only scope, then separately confirm one invocation.";
        }
        else if (action is "Invoke")
        {
            var own = ownedInvocation;
            var external = sharedInvocation;
            ownedInvocation = null;
            sharedInvocation = null;
            opened.Confirmation.Text = "";
            var receipt = own is not null ? await owned.InvokeAsync(own, new(RequestOrigin.LocalUi, Eligible), Current, token)
                : external is not null ? await shared.InvokeAsync(external, Current, token)
                : throw new InvalidOperationException("Preview a fresh exact invocation before confirming.");
            if (Current())
            {
                opened.Status.Text = $"Retained in the exact selected conversation: {receipt.Message.Outcome} / {receipt.Message.Reason}. "
                    + (receipt.Message.AssistantResponse?.Body ?? "No assistant answer. Inspect that conversation; no automatic retry.");
            }
        }
        else { throw new InvalidOperationException("Unknown named skill operation."); }
    }

    private bool Current() => Eligible() && choice?.Current() == true;

    private void RetirePreparation()
    {
        if (ownedInvocation is not null || sharedInvocation is not null)
        { ownedReview?.Dispose(); sharedReview?.Dispose(); }
        ownedInvocation?.Dispose();
        sharedInvocation?.Dispose();
        ownedInvocation = null;
        sharedInvocation = null;
        if (window is { } opened) { opened.Confirmation.Text = ""; opened.Confirm.IsEnabled = false; }
    }

    private void RetireReview()
    {
        RetirePreparation();
        ownedReview?.Dispose();
        sharedReview?.Dispose();
        ownedReview = null;
        sharedReview = null;
        choice = null;
        next = null;
        window?.ClearReview();
    }

    private void OnPrivacyClosed(object? sender, EventArgs args) => Retire();
    private void OnMainChanged(object? sender, PropertyChangedEventArgs args) { if (!Eligible()) { Retire(); } }

    private void Retire()
    {
        original?.Retire();
        lifetime?.Cancel();
        candidates?.Dispose();
        candidates = null;
        RetireReview();
        var opened = window;
        window = null;
        opened?.Clear();
        opened?.Close();
        original = null;
        name = "";
    }

    internal async Task CloseAndWait()
    {
        Retire();
#pragma warning disable VSTHRD003 // Logical cancellation is not physical I/O/model completion.
        await active;
#pragma warning restore VSTHRD003
        lifetime?.Dispose();
        lifetime = null;
    }

    public async ValueTask DisposeAsync()
    {
        disposed = true;
        await CloseAndWait();
        main.NamedSkillInvocationRequested -= Route;
        main.BindNamedSkillUseShutdown(null);
        main.BindNamedSkillChoiceCapture(null);
        main.PrivacyClosureRequested -= OnPrivacyClosed;
        main.PropertyChanged -= OnMainChanged;
    }
}
