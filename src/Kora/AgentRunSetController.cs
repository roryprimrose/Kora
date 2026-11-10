using System.ComponentModel;

using Avalonia.Controls;

using Kora.Application.Agents;
using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class AgentRunSetController(MainViewModel main, AgentRunSetFactory factory,
    SessionWorkspaceService workspace, Func<bool> ownsHost, Func<long> ownerRevision,
    ILogger<AgentRunSetController> logger)
{
    private AgentRunSetView? window;
    private AgentRunSetWindow? adapter;
    private Func<Window>? getWorkbench;
    private Action<AgentRunSetView>? presentWorkbench;
    private readonly bool[] invoking = new bool[2];
    internal AgentRunSetView? View => window;
    internal void BindWorkbench(Func<Window> ownerWindow, Action<AgentRunSetView> present)
    { getWorkbench = ownerWindow; presentWorkbench = present; }
    private AgentRunSet? runs;
    private readonly Task[] operations = [Task.CompletedTask, Task.CompletedTask];
    private Task cleanup = Task.CompletedTask;
    private long owner;
    private long call;
    private bool closed;

    internal Task ObserveOperation(int number)
    {
        if (number is not (1 or 2)) { throw new ArgumentOutOfRangeException(nameof(number)); }
#pragma warning disable VSTHRD003 // Expose only this controller's exact already-owned operation for lifecycle observation.
        return operations[number - 1];
#pragma warning restore VSTHRD003
    }

    internal void Bind()
    {
        main.AgentRunSetRequested += Route;
        main.PrivacyClosureRequested += PrivacyClosed;
        main.PropertyChanged += MainChanged;
        main.BindAgentRunSetShutdown(Shutdown);
    }

    private bool Eligible() => !closed && window is not null && ownsHost() && ownerRevision() == owner
        && main.CallPolicyRevision == call && main.CanRevealPrivatePresentation
        && main.IsSkillReviewCallEligible && !main.IsResponseInteractionPending;

    private async Task Route(AgentRunSetCommand command, RequestOrigin origin, Func<bool> eligible)
    {
        try
        {
            if (!eligible()) { throw new InvalidOperationException("Original command eligibility expired."); }
            if (command.Action is "Close") { await CloseAndWait(); return; }
            if (command.Action is "Open")
            {
                await CloseAndWait();
                closed = false;
                owner = ownerRevision();
                call = main.CallPolicyRevision;
                runs = factory.Create();
                var created = runs;
#pragma warning disable MA0147 // Native button callbacks have a void boundary; Route owns and reports their asynchronous work.
                void Execute(AgentRunSetCommand nextCommand) => Start(nextCommand, () => ReferenceEquals(runs, created) && Eligible());
                if (getWorkbench is not null && presentWorkbench is not null)
                { window = new(getWorkbench(), Execute, () => main.CanRevealPrivatePresentation); }
                else
                {
                    adapter = new(Execute);
                    window = adapter.View;
                }
#pragma warning restore MA0147
                window.Closed += (_, _) => Close();
                if (getWorkbench is not null)
                {
                    var presented = window;
                    window.PresentationLost += (_, _) =>
                    {
                        if (!ReferenceEquals(window, presented)) { return; }
                        for (var index = 0; index < presented.Runs.Length; index++)
                        {
                            presentations[index] = null;
                            if (!invoking[index]) { Retire(index + 1, presented.Runs[index]); }
                        }
                    };
                }
                for (var index = 0; index < 2; index++)
                {
                    var number = index + 1;
                    var panel = window.Runs[index];
                    panel.Source.PropertyChanged += (_, args) => Changed(number, panel, args, false);
                    panel.Input.PropertyChanged += (_, args) => Changed(number, panel, args, true);
                    panel.Targets.SelectionChanged += (_, _) => Retire(number, panel);
                }
                if (presentWorkbench is not null) { presentWorkbench(window); }
                else { adapter!.Show(); }
                if (!Eligible() || !eligible()) { Close(); }
                return;
            }
            if (!Eligible() || command.Number is not (1 or 2))
            { throw new InvalidOperationException("Open independent agents and select run 1 or run 2."); }
            var opened = window!;
            if (presentWorkbench is not null) { presentWorkbench(opened); }
            var panelRun = opened.Runs[command.Number - 1];
            opened.Selection.SelectedIndex = command.Number - 1;
            if (command.Action is "Select") { return; }
            if (command.Action is "Status") { ShowStatus(command.Number, panelRun); return; }
            if (command.Action is "Stop")
            {
                Retire(command.Number, panelRun);
                var revision = panelRun.Revision;
                await runs!.StopAsync(command.Number);
                if (Eligible() && eligible() && ReferenceEquals(window, opened) && panelRun.Revision == revision)
                { panelRun.Status.Text = "Stopped; this run's workflow and source resources are joined."; }
                return;
            }
            if (command.Action is "Source") { panelRun.Source.Text = command.Argument; Retire(command.Number, panelRun); return; }
            if (command.Action is "Request")
            { panelRun.Input.Text = command.Argument; Retire(command.Number, panelRun); panelRun.InputOrigin = origin; return; }
            if (command.Action is "Target")
            {
                var matches = (panelRun.Targets.ItemsSource as AgentRunSetView.Target[] ?? [])
                    .Where(item => string.Equals(item.Label, command.Argument, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length != 1) { throw new InvalidOperationException("Choose one unambiguous displayed Active conversation name using native selection."); }
                panelRun.Targets.SelectedItem = matches[0];
                return;
            }
            var indexRun = command.Number - 1;
            if (!operations[indexRun].IsCompleted)
            { throw new InvalidOperationException("This run is still owned; Stop joins it without affecting the other run."); }
            operations[indexRun] = RunReported(command, origin, eligible);
            if (command.Action is not "Invoke") { await Join(operations[indexRun]); }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failed(logger, exception.GetType().Name);
            main.ReportHostInteractionFailure("Independent agent run unavailable, cancelled or unconfirmed. No automatic retry; inspect the exact conversation.");
        }
    }

    private void Changed(int number, AgentRunSetView.RunPanel panel,
        Avalonia.AvaloniaPropertyChangedEventArgs args, bool input)
    {
        if (args.Property != TextBox.TextProperty) { return; }
        Retire(number, panel);
        if (input) { panel.InputOrigin = RequestOrigin.LocalUi; }
    }

    private void Retire(int number, AgentRunSetView.RunPanel panel)
    {
        if (!ReferenceEquals(window?.Runs[number - 1], panel)) { return; }
        presentations[number - 1] = null;
        panel.Revision++;
        panel.ClearReview();
        runs?.Revoke(number);
        panel.Status.Text = "Review revoked. Resources remain owned until Stop or fresh Review joins them.";
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100",
        Justification = "Native event boundary; Route owns, joins and reports every operation failure.")]
    private async void Start(AgentRunSetCommand command, Func<bool> eligible) =>
        await Route(command, RequestOrigin.LocalUi, eligible);

    private async Task RunReported(AgentRunSetCommand command, RequestOrigin origin, Func<bool> eligible)
    {
        var opened = window!;
        var captured = runs!;
        var panel = opened.Runs[command.Number - 1];
        var revision = panel.Revision;
        var indexRun = command.Number - 1;
        var native = opened.CapturePresentation();
        bool Current() => Eligible() && eligible() && ReferenceEquals(window, opened) && panel.Revision == revision
            && (invoking[indexRun] || native());
        try
        {
            using var activity = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Desktop, HostOperation.Request);
            if (command.Action is "Targets" or "NextTargets")
            {
                Guid? after = command.Action is "Targets" ? null : panel.Next
                    ?? throw new InvalidOperationException("No next page; refresh conversations.");
                Retire(command.Number, panel);
                revision = panel.Revision;
                var page = await workspace.ReadMetadataAsync(after, 25, CancellationToken.None);
                if (!Current()) { return; }
                panel.Next = page.Next;
                var records = page.Records.Where(item => item.Authority.IsActive).ToArray();
                panel.Targets.ItemsSource = records.Select((item, index) => new AgentRunSetView.Target(item,
                    item.Metadata?.Name.Value ?? $"Unnamed conversation {index + 1}")).ToArray();
                panel.Status.Text = "Select an existing Active conversation. No session was created or resumed.";
            }
            else if (command.Action is "Review")
            {
                Retire(command.Number, panel);
                revision = panel.Revision;
                await captured.StopAsync(command.Number);
                if (!Current()) { throw new InvalidOperationException("Original review eligibility expired while joining resources."); }
                var target = (panel.Targets.SelectedItem as AgentRunSetView.Target)?.Entry
                    ?? throw new InvalidOperationException("Select a distinct existing Active conversation.");
                panel.Status.Text = "Reading the explicitly selected profile.";
                await captured.ReviewAsync(command.Number, panel.Source.Text ?? string.Empty, target,
                    panel.Input.Text ?? string.Empty, panel.InputOrigin, Current, CancellationToken.None);
                if (!Current()) { captured.Revoke(command.Number); return; }
                var snapshot = captured.Read(command.Number)!;
                presentations[command.Number - 1] = new(captured, panel, revision, target, snapshot.Review!);
                if (ReadCurrentPresentation(command.Number, panel) is { } reviewed) { ShowReview(reviewed, panel); }
            }
            else if (command.Action is "Resolve")
            {
                RequireCurrentPresentation(command.Number, panel);
                await captured.ResolveAsync(command.Number, origin, CancellationToken.None);
                if (Current() && ReadCurrentPresentation(command.Number, panel) is { } resolved) { ShowReview(resolved, panel); }
            }
            else if (command.Action is "Invoke")
            {
                RequireCurrentPresentation(command.Number, panel);
                panel.Confirm.IsEnabled = panel.Resolve.IsEnabled = false;
                panel.Status.Text = "Invocation requested; canonical admission may report unavailable or resource-blocked. No running state inferred.";
                invoking[indexRun] = true;
                await captured.InvokeAsync(command.Number, origin, CancellationToken.None);
                if (Current() && native()) { ShowStatus(command.Number, panel); }
            }
            else { throw new InvalidOperationException("Unsupported independent agent command."); }
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failed(logger, exception.GetType().Name);
            if (Current() && native())
            {
                presentations[command.Number - 1] = null;
                panel.ClearReview();
                panel.Status.Text = "Unavailable, resource-blocked, revoked, cancelled or unconfirmed. Inspect the selected conversation. No success or rollback claimed.";
            }
        }
        finally { invoking[indexRun] = false; }
    }

    private static void ShowReview(AgentRunSet.AgentRunSnapshot snapshot, AgentRunSetView.RunPanel panel)
    {
        var review = snapshot.Review!;
        panel.Review.Text = $"LOCAL TEXT -> TEXT; NO TOOLS\n{review.Profile.Document.Text}\n\nORIGINAL TASK\n{review.Input}\n\n"
            + string.Join("\n\n", review.References?.Bundle?.Instructions.Select(item => item.Document.Text) ?? []);
        panel.Resolve.IsEnabled = review.References is not null && !review.IsInvocationReady;
        panel.Confirm.IsEnabled = review.IsInvocationReady;
        panel.Status.Text = review.IsInvocationReady
            ? "Review the complete inert profile/instructions and original task, then separately confirm this run once."
            : "Review declared instruction IDs and enabled candidates before separately consenting to their source reads.";
        if (review.References is { Bundle: null } references)
        {
            panel.Review.Text += "\nDECLARED IDs\n" + string.Join("\n", review.Profile.References.Select(item => item.Id))
                + "\nENABLED CANDIDATES\n" + string.Join("\n", references.Candidates.Select(item => item.Name + " (" + item.Id + ")"));
        }
    }

    private void PrivacyClosed(object? sender, EventArgs args) => Close();
    private void MainChanged(object? sender, PropertyChangedEventArgs args) { if (!Eligible()) { Close(); } }

    private void Close()
    {
        if (window is null && runs is null) { return; }
        closed = true;
        var opened = window;
        var captured = runs;
        window = null;
        adapter = null;
        runs = null;
        Array.Clear(presentations);
        var prior = cleanup;
        var pending = operations.ToArray();
        var resourceClose = captured?.DisposeAsync().AsTask() ?? Task.CompletedTask;
        opened?.Clear();
        opened?.Close();
#pragma warning disable VSTHRD003 // Join this controller's previous closure, current resources and both published operation handles.
        cleanup = Task.WhenAll([prior, resourceClose, .. pending]);
#pragma warning restore VSTHRD003
    }

    private async Task CloseAndWait()
    {
        Close();
#pragma warning disable VSTHRD003 // This aggregate was published by Close and retains every owned resource until termination.
        await Join(cleanup);
#pragma warning restore VSTHRD003
    }

    private static async Task Join(Task operation)
    {
#pragma warning disable VSTHRD003 // Join only the explicitly owned operation or aggregate closure.
        await operation;
#pragma warning restore VSTHRD003
    }

    private async Task Shutdown()
    {
        try { await CloseAndWait(); }
        finally
        {
            main.AgentRunSetRequested -= Route;
            main.PrivacyClosureRequested -= PrivacyClosed;
            main.PropertyChanged -= MainChanged;
            main.BindAgentRunSetShutdown(null);
        }
    }
}
