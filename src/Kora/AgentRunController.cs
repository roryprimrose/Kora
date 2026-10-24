using System.ComponentModel;

using Kora.Application.Agents;
using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class AgentRunController(MainViewModel main, BoundedAgentWorkflow workflow,
    SessionWorkspaceService workspace, Func<bool> ownsHost, Func<long> ownerRevision,
    ILogger<AgentRunController> logger)
{
    private AgentRunWindow? window;
    private BoundedAgentReview? review;
    private CancellationTokenSource? cancellation;
    private Task active = Task.CompletedTask;
    private Task cleanup = Task.CompletedTask;
    private long capturedOwner;
    private long capturedCall;
    private long inputRevision;
    private Guid? next;
    private RequestOrigin origin;
    private RequestOrigin inputOrigin;
#pragma warning disable VSTHRD003 // Exposes only this controller's already-owned operation for lifecycle observation.
    internal Task ActiveOperation => active;
#pragma warning restore VSTHRD003

    internal void Bind()
    {
        main.AgentRunRequested += Route;
        main.PrivacyClosureRequested += PrivacyClosed;
        main.PropertyChanged += MainChanged;
        main.BindAgentShutdown(Shutdown);
    }
    private bool Eligible() => window is not null && ownsHost() && ownerRevision() == capturedOwner
        && main.CallPolicyRevision == capturedCall && main.CanRevealPrivatePresentation
        && main.IsSkillReviewCallEligible && !main.IsResponseInteractionPending;

    private async Task Route(AgentRunCommand command, RequestOrigin requestOrigin, Func<bool> eligible)
    {
        try
        {
            if (!eligible()) { throw new InvalidOperationException("Original command admission expired."); }
            if (command.Action is "Stop") { await CloseAndWait(); return; }
            if (command.Action is "Open")
            {
                await CloseAndWait();
                capturedOwner = ownerRevision();
                capturedCall = main.CallPolicyRevision;
                origin = requestOrigin;
                inputOrigin = requestOrigin;
                cancellation = new();
#pragma warning disable MA0147 // Native buttons require void callbacks; Start joins RunReported, which logs failures.
                window = new(Start);
#pragma warning restore MA0147
                window.Closed += (_, _) => Close();
                window.Source.PropertyChanged += InputChanged;
                window.Input.PropertyChanged += InputChanged;
                window.Targets.SelectionChanged += (_, _) => Retire();
                window.Show();
                if (!Eligible() || !eligible()) { Close(); }
                return;
            }
            if (!Eligible() || !active.IsCompleted)
            { throw new InvalidOperationException("Open an exact current agent review and wait for its owned operation."); }
            origin = requestOrigin;
            if (command.Action is "Source") { window!.Source.Text = command.Argument; Retire(); origin = requestOrigin; return; }
            if (command.Action is "Request") { window!.Input.Text = command.Argument; Retire(); inputOrigin = requestOrigin; return; }
            if (command.Action is "Target")
            {
                var rows = window!.Targets.ItemsSource as AgentTarget[];
                window.Targets.SelectedItem = rows?.SingleOrDefault(row =>
                    string.Equals(row.Entry.Authority.SessionId.Value.ToString("D"), command.Argument, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("Select an exact displayed Active conversation ID; refresh its page first.");
                return;
            }
            active = RunReported(command.Action, eligible);
            if (command.Action is "Invoke") { return; }
            await JoinActive();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failed(logger, exception.GetType().Name);
            main.ReportHostInteractionFailure("Agent run unavailable, revoked or unconfirmed. Inspect the exact conversation; no automatic retry.");
        }
    }

    private void InputChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == Avalonia.Controls.TextBox.TextProperty)
        {
            Retire();
            if (ReferenceEquals(sender, window?.Input)) { inputOrigin = RequestOrigin.LocalUi; }
        }
    }
    private void Retire()
    {
        inputRevision++;
        review?.Dispose();
        if (window is not null) { window.Confirm.IsEnabled = false; window.Review.Text = string.Empty; window.Result.Text = string.Empty; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "Native button boundary; owned RunReported catches and logs failures before JoinActive completes.")]
    private async void Start(string action)
    {
        if (action is "Stop") { Close(); return; }
        if (!active.IsCompleted) { return; }
        origin = RequestOrigin.LocalUi;
        active = RunReported(action, Eligible);
        await JoinActive();
    }

    private async Task RunReported(string action, Func<bool> eligible)
    {
        try
        {
            var opened = window ?? throw new InvalidOperationException("Agent review is closed.");
            var token = cancellation!.Token;
            var revision = inputRevision;
            bool Current() => Eligible() && eligible() && ReferenceEquals(window, opened) && inputRevision == revision;
            if (!Current()) { throw new InvalidOperationException("Private host admission changed."); }
            using var activity = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Desktop, HostOperation.Request);
            if (action is "Targets" or "NextTargets")
            {
                Guid? after = action is "Targets" ? null : next ?? throw new InvalidOperationException("No next page. Refresh conversations.");
                Retire();
                var page = await workspace.ReadMetadataAsync(after, 25, token);
                if (!Eligible() || !eligible() || !ReferenceEquals(window, opened)) { return; }
                next = page.Next;
                opened.Targets.ItemsSource = page.Records.Where(item => item.Authority.IsActive).Select(item => new AgentTarget(item)).ToArray();
                opened.Status.Text = "Explicitly select one existing Active conversation. None was created or resumed.";
            }
            else if (action is "Review")
            {
                Retire();
                revision = inputRevision;
                var target = (opened.Targets.SelectedItem as AgentTarget)?.Entry
                    ?? throw new InvalidOperationException("Select one current Active conversation.");
                review = await workflow.ReviewAsync(opened.Source.Text ?? string.Empty, target,
                    opened.Input.Text ?? string.Empty, inputOrigin, Current, token);
                if (!Current()) { await workflow.RevokeAsync(review); review = null; return; }
                var profile = review.Profile;
                opened.Review.Text = $"{profile.Document.Definition.Name}\nSOURCE {opened.Source.Text}\n"
                    + $"SESSION {target.Authority.SessionId.Value:D}\nLOCAL TEXT -> TEXT; REFERENCES NONE\n"
                    + $"TOOLS {string.Join(", ", profile.Tools)}\n"
                    + $"HOST CLAMPS: calls {profile.MaximumCalls}, rounds {profile.MaximumRounds}, "
                    + $"input {profile.MaximumInputUtf8Bytes} bytes, output {profile.MaximumOutputUtf8Bytes} bytes, "
                    + $"deadline {profile.Deadline.TotalMilliseconds}ms\n"
                    + $"REQUESTED: calls {profile.RequestedCalls}, rounds {profile.RequestedRounds}, "
                    + $"input {profile.RequestedInputUtf8Bytes}, output {profile.RequestedOutputUtf8Bytes}, "
                    + $"deadline {profile.RequestedDeadlineMilliseconds}ms\n\n"
                    + $"INERT PROFILE\n{profile.Document.Definition.Content}\n\nORIGINAL INPUT\n{review.Input}";
                opened.Confirm.IsEnabled = true;
                opened.Status.Text = "Review exact input, profile, locality, tools and clamped budgets. Separately consent to invoke once.";
            }
            else if (action is "Invoke")
            {
                var captured = review ?? throw new InvalidOperationException("Review the exact invocation first.");
                opened.Confirm.IsEnabled = false;
                opened.Status.Text = "Executing one serial bounded run. Stop/revoke remains available; resource release requires actual quiescence.";
                var result = await workflow.InvokeAsync(captured, token);
                if (!Current()) { return; }
                opened.Result.Text = result.Message.AssistantResponse?.Body ?? "No assistant answer was produced.";
                opened.Status.Text = $"Retained terminal {result.Message.Outcome} / {result.Message.Reason}; "
                    + $"request {result.Message.Request.RequestId.Value:D}. Inspect it in Sessions. No automatic replay.";
            }
            else { throw new InvalidOperationException("Use an exact agent review, source, request, target, review invocation, confirm invocation or stop command."); }
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failed(logger, exception.GetType().Name);
            review?.Dispose();
            if (window is { } opened)
            {
                opened.Confirm.IsEnabled = false;
                opened.Result.Text = string.Empty;
                opened.Status.Text = "Unavailable, revoked, cancelled or unconfirmed. Inspect Sessions before any fresh review. No success or rollback claimed.";
            }
        }
    }

    private void PrivacyClosed(object? sender, EventArgs args) => Close();
    private void MainChanged(object? sender, PropertyChangedEventArgs args) { if (!Eligible()) { Close(); } }
    private void Close()
    {
        if (window is null && review is null) { return; }
        var opened = window;
        var captured = review;
        window = null;
        review = null;
        captured?.Dispose();
        cancellation?.Cancel();
        opened?.Clear();
        opened?.Close();
        cleanup = ReleaseAfterAsync(active, captured);
    }
    private async Task ReleaseAfterAsync(Task running, BoundedAgentReview? captured)
    {
        try
        {
#pragma warning disable VSTHRD003 // Join the exact operation captured before closure; retain native source ownership meanwhile.
            await running;
#pragma warning restore VSTHRD003
            if (captured is not null) { await workflow.RevokeAsync(captured); }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failed(logger, exception.GetType().Name);
            main.ReportHostInteractionFailure("Agent resource release is unconfirmed. No replay or successful cancellation claimed.");
        }
    }
    private async Task JoinActive()
    {
#pragma warning disable VSTHRD003 // Join exactly this controller's already-owned operation.
        await active;
#pragma warning restore VSTHRD003
    }
    private async Task CloseAndWait()
    {
        Close();
        await JoinActive();
#pragma warning disable VSTHRD003 // Join exactly the source-release operation started by Close.
        await cleanup;
#pragma warning restore VSTHRD003
        cancellation?.Dispose();
        cancellation = null;
    }
    private async Task Shutdown()
    {
        await CloseAndWait();
        await workflow.DisposeAsync();
        main.AgentRunRequested -= Route;
        main.PrivacyClosureRequested -= PrivacyClosed;
        main.PropertyChanged -= MainChanged;
        main.BindAgentShutdown(null);
    }
    private sealed record AgentTarget(SessionWorkspaceEntry Entry)
    {
        public override string ToString() => Entry.Authority.SessionId.Value.ToString("D");
    }
}
