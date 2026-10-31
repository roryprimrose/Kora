using System.ComponentModel;
using Kora.Application.Skills;
using Kora.Application.ViewModels;
using Kora.Core.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Skills;
using Kora.Core.Interaction;
using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class OwnedSkillInvocationController(
    MainViewModel main, OwnedSkillInvocationWorkflow workflow, Func<bool> ownsHost,
    Func<long> ownershipRevision, ILogger<OwnedSkillInvocationController> logger,
    ReviewedToolSkillInvocationWorkflow? toolWorkflow = null, QuestionWindowController? questions = null) : IAsyncDisposable
{
    private OwnedSkillInvocationWindow? window;
    private OwnedSkillUseReview? review;
    private OwnedSkillInvocation? invocation;
    private ReviewedToolSkillReview? toolReview;
    private ReviewedToolSkillInvocation? toolInvocation;
    private OwnedSkillAuthoringAuthority? original;
    private OwnedSkillAuthoringAuthority? requestAuthority;
    private CancellationTokenSource? lifetime;
    private Task active = Task.CompletedTask;
    private long ownerRevision;
    private long callRevision;
    private long inputRevision;
    private Guid? next;
    private bool disposed;

#pragma warning disable VSTHRD003 // Observe the single owned operation, including cancellation-ignoring work.
    internal Task ActiveOperation => active;
#pragma warning restore VSTHRD003

    internal void Bind()
    {
        main.PrivacyClosureRequested += OnPrivacyClosed;
        main.PropertyChanged += OnMainChanged;
    }

    private bool Eligible() => !disposed && ownsHost() && ownershipRevision() == ownerRevision
        && main.CallPolicyRevision == callRevision && main.CanRevealPrivatePresentation && main.IsSkillReviewCallEligible
        && !main.IsResponseInteractionPending && original?.IsCurrent() == true && lifetime?.IsCancellationRequested == false;

    internal async Task Handle(OwnedSkillAuthoringCommand command, OwnedSkillAuthoringAuthority authority,
        Func<bool> sourceEligible)
    {
        if (command.Operation is "close-use")
        { await CloseAndWait(); return; }
        if (!active.IsCompleted) { throw new InvalidOperationException("Wait for owned skill use I/O to join, or cancel it."); }
        active = RunReported(command, authority, sourceEligible);
#pragma warning disable VSTHRD003 // Retain the native operation until all admitted work terminates.
        await active;
#pragma warning restore VSTHRD003
    }

    private async Task RunReported(OwnedSkillAuthoringCommand command, OwnedSkillAuthoringAuthority authority,
        Func<bool> sourceEligible)
    {
        try
        {
            if (command.Operation is "use")
            {
                Retire();
                lifetime?.Dispose();
                lifetime = new();
                original = authority;
                requestAuthority = null;
                ownerRevision = ownershipRevision();
                callRevision = main.CallPolicyRevision;
                var captured = await workflow.ReviewAsync(command.Id!, command.Version!, authority,
                    () => Eligible() && sourceEligible(), lifetime.Token);
                if (captured.Tool == OwnedSkillToolSelection.ReadOnlyObservations)
                { captured.Dispose(); throw new InvalidOperationException("Use owned skill workflow review for this exact observation workflow; the ordinary guidance/model route does not execute its steps."); }
                if (!Eligible() || !sourceEligible()) { captured.Dispose(); return; }
                review = captured;
                var opened = new OwnedSkillInvocationWindow(action => Start(action), captured.Tool == OwnedSkillToolSelection.WebPageGet);
                window = opened;
                opened.Instructions.Text = captured.Text;
                opened.LiteralInputs.Configure(captured.RequiredNames, (_, _) =>
                {
                    RetireConfirmation();
                    requestAuthority = new(RequestOrigin.LocalUi, Eligible);
                });
                opened.Request.PropertyChanged += (_, args) =>
                {
                    if (args.Property == Avalonia.Controls.TextBox.TextProperty)
                    { RetireConfirmation(); requestAuthority = new(RequestOrigin.LocalUi, Eligible); }
                };
                opened.Address.PropertyChanged += (_, args) =>
                { if (args.Property == Avalonia.Controls.TextBox.TextProperty) { RetireConfirmation(); } };
                opened.Targets.SelectionChanged += (_, _) => { RetireConfirmation(); toolReview?.Dispose(); toolReview = null; };
                opened.Closed += (_, _) => Retire();
                opened.Show();
                if (!Eligible()) { Retire(); return; }
                opened.Status.Text = "Review both complete files, then separately Enable. Nothing invoked.";
                return;
            }
            var currentWindow = window ?? throw new InvalidOperationException("Review an exact saved ID/version with owned skill use first.");
            var currentReview = review ?? throw new InvalidOperationException("The exact source review is unavailable.");
            bool Current() => Eligible() && sourceEligible() && authority.IsCurrent()
                && ReferenceEquals(window, currentWindow) && currentReview.IsCurrent;
            if (!Current()) { throw new InvalidOperationException("Owned skill use authority expired. Review again."); }
            var token = lifetime!.Token;
            switch (command.Operation)
            {
                case "enable":
                    if (currentReview.Tool == OwnedSkillToolSelection.WebPageGet)
                    {
                        await RequireToolWorkflow().EnableAsync(toolReview
                            ?? throw new InvalidOperationException("Select a target and review the exact tool revision first."),
                            Current, authority.Origin, token);
                    }
                    else { await workflow.EnableAsync(currentReview, authority, Current, token); }
                    if (Current()) { currentWindow.Status.Text = "Exact revision enabled once, until " + currentReview.ExpiresAt.ToLocalTime() + ". Select an Active target and enter the original request."; }
                    break;
                case "targets":
                case "next-targets":
                    RetireConfirmation();
                    currentWindow.Targets.ItemsSource = null;
                    var page = await workflow.ReadTargetsAsync(command.Operation is "next-targets"
                        ? next ?? throw new InvalidOperationException("No next page. Refresh targets.") : null, authority, Current, token);
                    if (!Current()) { return; }
                    next = page.Next;
                    currentWindow.Targets.ItemsSource = page.Records.Where(row => row.Authority.IsActive)
                        .Select(row => new OwnedSkillConversationTarget(row)).ToArray();
                    currentWindow.Status.Text = "Select exactly one displayed Active conversation; no conversation was created or resumed.";
                    break;
                case "target":
                    var rows = (currentWindow.Targets.ItemsSource as OwnedSkillConversationTarget[]) ?? [];
                    currentWindow.Targets.SelectedItem = rows.SingleOrDefault(row =>
                        string.Equals(row.Entry.Authority.SessionId.Value.ToString("D"), command.Literal, StringComparison.Ordinal))
                        ?? throw new InvalidOperationException("Select an exact Active target from the displayed current page.");
                    break;
                case "request":
                    currentWindow.Request.Text = command.Literal;
                    requestAuthority = authority;
                    break;
                case "input":
                    if (command.InputName is null || !currentWindow.LiteralInputs.Inputs.TryGetValue(command.InputName, out var input))
                    { throw new InvalidDataException("Select an exact declared required literal input name."); }
                    input.Text = command.Literal;
                    requestAuthority = authority;
                    break;
                case "url":
                    currentWindow.Address.Text = command.Literal;
                    break;
                case "review-tool":
                    RetireConfirmation(false);
                    if (toolReview is not null)
                    { throw new InvalidOperationException("This target/tool revision was already reviewed. Start a fresh exact source review instead of renewing it."); }
                    if (currentReview.Tool != OwnedSkillToolSelection.WebPageGet)
                    { throw new InvalidOperationException("The instruction-only revision does not declare a host tool."); }
                    var toolTarget = (currentWindow.Targets.SelectedItem as OwnedSkillConversationTarget)?.Entry
                        ?? throw new InvalidOperationException("Select exactly one current Active conversation.");
                    toolReview = await RequireToolWorkflow().ReviewOwnedAsync(workflow, currentReview, toolTarget,
                        authority, Current, token);
                    currentWindow.Status.Text = "Kora-owned source, exact target and original channel reviewed. Separately Enable; web access still needs per-action approval.";
                    break;
                case "review-use":
                    RetireConfirmation(false);
                    var target = (currentWindow.Targets.SelectedItem as OwnedSkillConversationTarget)?.Entry
                        ?? throw new InvalidOperationException("Select exactly one current Active conversation.");
                    var inputAuthority = requestAuthority ?? authority;
                    if (currentReview.RequiredNames.Any(name =>
                        string.IsNullOrWhiteSpace(currentWindow.LiteralInputs.Inputs[name].Text)))
                    {
                        await CollectMissingInputs(currentWindow, currentReview, target, inputAuthority, Current, token);
                    }
                    var exact = inputRevision;
                    bool Exact() => Current() && inputRevision == exact;
                    using (var request = HostActivity.BeginRoot(HostRequest.Create(inputAuthority.Origin),
                        HostActivityLayer.Desktop, HostOperation.Request))
                    {
                        if (currentReview.Tool == OwnedSkillToolSelection.WebPageGet)
                        {
                            var reviewedTool = toolReview ?? throw new InvalidOperationException("Review and enable the exact tool revision first.");
                            if (!Uri.TryCreate(currentWindow.Address.Text, UriKind.Absolute, out var destination))
                            { throw new InvalidOperationException("Enter one explicit absolute public HTTP or HTTPS URL."); }
                            toolInvocation = await RequireToolWorkflow().PrepareAsync(reviewedTool,
                                currentWindow.Request.Text ?? "", destination, Exact, inputAuthority.Origin, token);
                            currentWindow.Confirmation.Text = $"{reviewedTool.SourceDescription}\n{reviewedTool.Text}\n"
                                + $"TARGET {target.Authority.SessionId.Value:D}\nORIGIN {reviewedTool.Origin}\nLocalOnly; at most ONE host WebPageGet call; "
                                + $"two model rounds; existing host byte limits and 15-second deadline.\nURL {destination.AbsoluteUri}\n"
                                + $"Separate per-action approval required.\nORIGINAL REQUEST\n{toolInvocation.Body}";
                            request.Complete(HostOperationOutcome.Completed);
                            if (!Exact()) { RetireConfirmation(); return; }
                            currentWindow.Confirm.IsEnabled = true;
                            currentWindow.Status.Text = "Review the exact owned source, target, request and URL, then separately confirm.";
                            break;
                        }
                        invocation = await workflow.PrepareAsync(currentReview, target, currentWindow.Request.Text ?? "",
                            inputAuthority, Exact, token, currentWindow.LiteralInputs.Capture());
                        request.Complete(HostOperationOutcome.Completed);
                    }
                    if (!Exact()) { invocation.Dispose(); invocation = null; return; }
                    currentWindow.Confirmation.Text = $"TARGET {target.Authority.SessionId.Value:D}\nLocalOnly; tools none\n{invocation.Preview}";
                    currentWindow.Confirm.IsEnabled = true;
                    currentWindow.Status.Text = "Review all serialized input, then separately confirm one invocation.";
                    break;
                case "invoke":
                    var prepared = invocation;
                    var preparedTool = toolInvocation;
                    if (preparedTool is not null && authority.Origin != preparedTool.Origin)
                    { throw new InvalidOperationException("Confirmation must preserve the exact reviewed original input channel."); }
                    invocation = null;
                    toolInvocation = null;
                    currentWindow.Confirm.IsEnabled = false;
                    var revision = inputRevision;
                    bool ExactInvocation() => Current() && authority.IsCurrent() && inputRevision == revision;
                    var receipt = preparedTool is not null
                        ? await RequireToolWorkflow().InvokeAsync(preparedTool, ExactInvocation,
                            async (proposal, generation, address, admitted, approvalToken) =>
                            {
                                if (questions is null) { throw new InvalidOperationException("Native per-action page approval is unavailable."); }
                                var spec = new QuestionSpec("Exact page approval", QuestionKind.SingleChoice, [new("once", "Approve once")]);
                                var presentation = await questions.AskConversationAsync(currentWindow, proposal.Request, generation,
                                    spec, proposal.ExpiresAt, admitted, approvalToken, proposal, address);
                                return new(presentation.Presented, presentation.Completed);
                            }, token)
                        : await workflow.InvokeAsync(prepared ?? throw new InvalidOperationException("Preview the exact invocation before confirming."),
                            authority, ExactInvocation, token);
                    if (Eligible() && ReferenceEquals(window, currentWindow))
                    {
                        currentWindow.Confirmation.Text = string.Empty;
                        currentWindow.Status.Text = $"Retained in selected conversation: {receipt.Message.Outcome} / {receipt.Message.Reason}. "
                            + (receipt.Message.AssistantResponse?.Body ?? "No answer. Inspect that conversation; no automatic retry.");
                    }
                    break;
                default: throw new InvalidOperationException(OwnedSkillAuthoringCommand.Syntax);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            UseFailed(logger, exception.GetType().Name);
            review?.Dispose();
            toolReview?.Dispose();
            toolInvocation?.Dispose();
            toolInvocation = null;
            invocation?.Dispose();
            invocation = null;
            if (window is { } opened)
            {
                opened.Clear();
                if (main.CanRevealPrivatePresentation)
                { opened.Status.Text = "Owned skill use unavailable or unconfirmed: " + exception.Message + " Inspect the selected conversation before a fresh review; no automatic retry."; }
            }
            main.ReportHostInteractionFailure("Owned skill use unavailable or unconfirmed; inspect the selected conversation before any fresh review.");
        }
    }

#pragma warning disable VSTHRD100 // Native event boundary; RunReported handles every operation failure explicitly.
    private async void Start(string action)
    {
        try { await Handle(new(action), new(RequestOrigin.LocalUi, Eligible), static () => true); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { UseFailed(logger, exception.GetType().Name); window?.Status.Text = exception.Message; }
    }
#pragma warning restore VSTHRD100

    private void RetireConfirmation(bool edited = true)
    {
        inputQuestions?.Cancel();
        Interlocked.Increment(ref inputRevision);
        if (edited && (invocation is not null || toolInvocation is not null)) { review?.Dispose(); toolReview?.Dispose(); }
        invocation?.Dispose();
        invocation = null;
        toolInvocation?.Dispose();
        toolInvocation = null;
        if (window is { } opened) { opened.Confirmation.Text = string.Empty; opened.Confirm.IsEnabled = false; }
    }

    private void OnPrivacyClosed(object? sender, EventArgs args) => Retire();
    private void OnMainChanged(object? sender, PropertyChangedEventArgs args) { if (!Eligible()) { Retire(); } }

    internal void Retire()
    {
        var opened = window;
        window = null;
        lifetime?.Cancel();
        review?.Dispose();
        review = null;
        toolReview?.Dispose();
        toolReview = null;
        toolInvocation?.Dispose();
        toolInvocation = null;
        invocation?.Dispose();
        invocation = null;
        opened?.Clear();
        opened?.Close();
    }

    private ReviewedToolSkillInvocationWorkflow RequireToolWorkflow() => toolWorkflow
        ?? throw new InvalidOperationException("The reviewed host tool skill pipeline is unavailable.");

    internal async Task CloseAndWait()
    {
        Retire();
#pragma warning disable VSTHRD003 // Never release cancellation-ignoring reads or model work on logical close.
        await active;
#pragma warning restore VSTHRD003
        lifetime?.Dispose();
        lifetime = null;
    }

    public async ValueTask DisposeAsync()
    {
        disposed = true;
        await CloseAndWait();
        main.PrivacyClosureRequested -= OnPrivacyClosed;
        main.PropertyChanged -= OnMainChanged;
    }
}
