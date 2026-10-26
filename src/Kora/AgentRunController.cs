using System.ComponentModel;

using Kora.Application.Agents;
using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Agents;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class AgentRunController(MainViewModel main, BoundedAgentWorkflow workflow,
    SessionWorkspaceService workspace, Func<bool> ownsHost, Func<long> ownerRevision,
    ILogger<AgentRunController> logger, NamedAgentResolver? namedAgents = null)
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
    private NamedAgentSelection? namedSelection;
    private Func<bool>? namedEligibility;
    private NamedAgentCommand? namedCommand;
    private long contentRevision;
    private long namedContentRevision;
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
    private bool HostEligible() => window is not null && ownsHost() && ownerRevision() == capturedOwner
        && main.CallPolicyRevision == capturedCall && main.CanRevealPrivatePresentation
        && main.IsSkillReviewCallEligible && !main.IsResponseInteractionPending;
    private bool Eligible() => HostEligible() && (namedEligibility?.Invoke() ?? true);

    private async Task Route(AgentRunCommand command, RequestOrigin requestOrigin, Func<bool> eligible)
    {
        try
        {
            if (!eligible()) { throw new InvalidOperationException("Original command admission expired."); }
            if (command.Action is "Stop") { await CloseAndWait(); return; }
            if (command.Action is "Open" or "Named")
            {
                var named = command.Action is "Named" ? NamedAgentCommand.Parse(command.Argument) : null;
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
                BindSharedSkillSelection(window);
                window.Show();
                if (!Eligible() || !eligible()) { Close(); }
                if (named is not null && window is { } opened)
                {
                    namedCommand = named;
                    namedEligibility = eligible;
                    opened.Source.IsReadOnly = opened.Input.IsReadOnly = true;
                    active = PrepareNamedAsync(named, opened, eligible);
                    await JoinActive();
                }
                return;
            }
            if (!Eligible() || !active.IsCompleted)
            { throw new InvalidOperationException("Open an exact current agent review and wait for its owned operation."); }
            origin = requestOrigin;
            if (SelectSharedSkill(command)) { return; }
            if (namedCommand is not null && command.Action is "Source" or "Request")
            { throw new InvalidOperationException("Start a fresh named-agent command to change its exact source or original task."); }
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

    private async Task PrepareNamedAsync(NamedAgentCommand command, AgentRunWindow opened, Func<bool> eligible)
    {
        try
        {
            var resolver = namedAgents ?? throw new InvalidOperationException("Named agent source lookup is unavailable in this host.");
            var token = cancellation!.Token;
            var selected = await resolver.ResolveAsync(command.Name,
                () => HostEligible() && eligible() && ReferenceEquals(window, opened), token);
            namedSelection = selected;
            namedEligibility = () => eligible() && selected.IsCurrent();
            opened.Source.Text = selected.Path;
            opened.Input.Text = command.Input;
            namedContentRevision = contentRevision;
            inputOrigin = origin;
            await ReadTargetsAsync("Targets", opened, eligible, token);
            if (!Eligible() || !ReferenceEquals(window, opened)) { return; }
            opened.Status.Text = $"Agent: {selected.Definition.Name}. Select one existing Active conversation by readable name, "
                + "review the complete exact source and original task, then separately confirm once. No model has run.";
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failed(logger, exception.GetType().Name);
            opened.Status.Text = "Named agent unavailable, missing, ambiguous or revoked. No fallback agent or execution. Start a fresh command.";
            main.ReportHostInteractionFailure("Named agent invocation unavailable. Review the registered sources and start a fresh exact command.");
        }
    }

    private async Task ReadTargetsAsync(string action, AgentRunWindow opened, Func<bool> eligible, CancellationToken token)
    {
        Guid? after = action is "Targets" ? null : next ?? throw new InvalidOperationException("No next page. Refresh conversations.");
        Retire();
        var page = await workspace.ReadMetadataAsync(after, 25, token);
        if (!Eligible() || !eligible() || !ReferenceEquals(window, opened))
        { throw new InvalidOperationException("Active conversation inspection admission expired."); }
        next = page.Next;
        opened.Targets.ItemsSource = page.Records.Where(item => item.Authority.IsActive).Select(item => new AgentTarget(item)).ToArray();
        opened.Status.Text = "Explicitly select one existing Active conversation. None was created or resumed.";
    }

    private void InputChanged(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == Avalonia.Controls.TextBox.TextProperty)
        {
            contentRevision++;
            Retire();
            if (ReferenceEquals(sender, window?.Input)) { inputOrigin = RequestOrigin.LocalUi; }
        }
    }
    private void Retire()
    {
        inputRevision++;
        review?.Dispose();
        if (window is not null)
        {
            window.Confirm.IsEnabled = window.Resolve.IsEnabled = false;
            window.RetireSharedSkills();
            window.Review.Text = string.Empty;
            window.Result.Text = string.Empty;
        }
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
            bool Current() => Eligible() && eligible() && ReferenceEquals(window, opened) && inputRevision == revision
                && (namedCommand is null || namedSelection is not null
                    && contentRevision == namedContentRevision);
            if (!Current()) { throw new InvalidOperationException("Private host admission changed."); }
            using var activity = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Desktop, HostOperation.Request);
            if (action is "Targets" or "NextTargets")
            {
                await ReadTargetsAsync(action, opened, eligible, token);
            }
            else if (action is "Review")
            {
                Retire();
                revision = inputRevision;
                var target = (opened.Targets.SelectedItem as AgentTarget)?.Entry
                    ?? throw new InvalidOperationException("Select one current Active conversation.");
                using (var original = HostActivity.BeginRoot(HostRequest.Create(inputOrigin), HostActivityLayer.Desktop,
                    HostOperation.Request, [new(activity.Activity!.Context)]))
                {
                    try
                    {
                        review = await workflow.ReviewAsync(opened.Source.Text ?? string.Empty, target,
                            opened.Input.Text ?? string.Empty, inputOrigin, Current, token);
                        original.Complete(HostOperationOutcome.Completed);
                    }
                    catch
                    {
                        original.Complete(token.IsCancellationRequested ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
                        throw;
                    }
                }
                namedSelection?.ValidateSource(review.Profile.Document);
                if (!Current()) { await workflow.RevokeAsync(review); review = null; return; }
                if (review.Skill is not null) { ShowSkillProfile(opened, review); }
                else if (review.References is { } referenced)
                {
                    opened.Review.Text = $"LOCAL TEXT -> TEXT\nCONVERSATION {new AgentTarget(target)}\n"
                        + DescribeToolsAndBudgets(review.Profile)
                        + $"DECLARED OWNED INSTRUCTION IDS\n{string.Join("\n", review.Profile.References.Select(item => item.Id))}\n"
                        + $"EXACT ENABLED CANDIDATES\n{string.Join("\n", referenced.Candidates.Select(item => item.Name + " (" + item.Id + ")"))}\n"
                        + $"COMPLETE PROFILE\n{review.Profile.Document.Text}\n\nORIGINAL INPUT\n{review.Input}";
                    opened.Resolve.IsEnabled = true;
                    opened.Status.Text = "No instruction source was read. Separately consent to read only these candidates; this is not invocation consent.";
                }
                else { ShowInvocation(opened, review); }
            }
            else if (action is "Resolve")
            {
                var captured = review ?? throw new InvalidOperationException("Review exact declarations and candidates first.");
                opened.Resolve.IsEnabled = false;
                await workflow.ResolveAsync(captured, origin, token);
                if (!Current()) { await workflow.RevokeAsync(captured); review = null; return; }
                ShowInvocation(opened, captured);
            }
            else if (await RunSharedSkillActionAsync(action, opened, Current,
                () => Eligible() && eligible() && ReferenceEquals(window, opened), token)) { }
            else if (action is "Invoke")
            {
                var captured = review ?? throw new InvalidOperationException("Review the exact invocation first.");
                opened.Confirm.IsEnabled = false;
                opened.Status.Text = "Executing one serial bounded run. Stop/revoke remains available; resource release requires actual quiescence.";
                var result = await workflow.InvokeAsync(captured, origin, token);
                if (!Current()) { return; }
                opened.Result.Text = result.Message.AssistantResponse?.Body ?? "No assistant answer was produced.";
                opened.Status.Text = $"Retained terminal {result.Message.Outcome} / {result.Message.Reason}. "
                    + "Inspect it in the selected conversation in Sessions. No automatic replay.";
            }
            else { throw new InvalidOperationException("Use an exact agent source, request, target, reference review, resolution consent, invocation review, confirm or stop command."); }
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failed(logger, exception.GetType().Name);
            review?.Dispose();
            if (window is { } opened)
            {
                opened.Confirm.IsEnabled = opened.Resolve.IsEnabled = false;
                opened.RetireSharedSkills();
                opened.Result.Text = string.Empty;
                opened.Status.Text = "Unavailable, revoked, cancelled or unconfirmed. Inspect Sessions before any fresh review. No success or rollback claimed.";
            }
        }
    }

    private static void ShowInvocation(AgentRunWindow opened, BoundedAgentReview captured)
    {
        var profile = captured.Profile;
        opened.Review.Text = $"{profile.Document.Definition.Name}\nSOURCE {opened.Source.Text}\n"
                    + $"CONVERSATION {new AgentTarget(captured.Target)}\nORIGINAL CHANNEL {captured.Origin}\nLOCAL TEXT -> TEXT\n"
                    + DescribeToolsAndBudgets(profile)
                    + $"COMPLETE INERT PROFILE (UTF-8 BOM: {profile.Document.HasBom})\n{profile.Document.Text}\n\n"
                    + string.Join("\n\n", captured.References?.Bundle?.Instructions.Select(item =>
                        $"COMPLETE INERT INSTRUCTION {item.Document.Definition.Id} (UTF-8 BOM: {item.Document.HasBom})\n{item.Document.Text}") ?? [])
                    + $"\n\nORIGINAL INPUT\n{captured.Input}";
                opened.Confirm.IsEnabled = true;
                opened.Resolve.IsEnabled = false;
                opened.Status.Text = "Review exact input, profile, locality, tools and clamped budgets. Separately consent to invoke once.";
    }

    private static string DescribeToolsAndBudgets(BoundedAgentProfile profile) =>
        $"TOOLS {profile.Document.Metadata["tools"]}; independently permission checked, never granted by guidance.\n"
        + $"CUMULATIVE HOST CLAMPS: calls {profile.MaximumCalls}, rounds {profile.MaximumRounds}, "
        + $"input {profile.MaximumInputUtf8Bytes} bytes, output {profile.MaximumOutputUtf8Bytes} bytes, "
        + $"deadline {profile.Deadline.TotalMilliseconds}ms\n"
        + $"REQUESTED: calls {profile.RequestedCalls}, rounds {profile.RequestedRounds}, "
        + $"input {profile.RequestedInputUtf8Bytes}, output {profile.RequestedOutputUtf8Bytes}, "
        + $"deadline {profile.RequestedDeadlineMilliseconds}ms\n\n";

    private void PrivacyClosed(object? sender, EventArgs args) => Close();
    private void MainChanged(object? sender, PropertyChangedEventArgs args) { if (!Eligible()) { Close(); } }
    private void Close()
    {
        if (window is null && review is null) { return; }
        var opened = window;
        var captured = review;
        window = null;
        review = null;
        namedSelection = null;
        namedEligibility = null;
        namedCommand = null;
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
        public override string ToString() => Entry.Metadata?.Name.Value ?? "Active conversation";
    }
}
