using System.Globalization;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Application.Infrastructure;
using Kora.Application.Timekeeping;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Timekeeping;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly StopwatchWorkflow? stopwatch;
    private string stopwatchDisplay = "Idle. Observed elapsed: 00:00:00. Not a live clock.";
    private Func<bool>? stopwatchRunAdmission;
    private long stopwatchNativeRevision;
    private bool stopwatchNativeAvailable;
    private bool stopwatchNativeVisible;
    private bool isStopwatchPanelVisible;

    public bool IsStopwatchPanelVisible => isStopwatchPanelVisible;

    public string StopwatchDisplay
    {
        get
        {
            if (stopwatchRunAdmission is { } admitted && !admitted()) { RetireStopwatch(); }
            return stopwatchDisplay;
        }
    }
    public AsyncCommand StopwatchStatusCommand { get; }
    public AsyncCommand StopwatchStopCommand { get; }
    public AsyncCommand StopwatchResetCommand { get; }

    public void BindStopwatchNativeLifetime(bool available)
    {
        stopwatchNativeAvailable = available && !disposed;
        stopwatchNativeVisible = available;
        Interlocked.Increment(ref stopwatchNativeRevision);
        if (!available) { RetireStopwatch(); }
    }

    public void ObserveStopwatchNativeVisibility(bool visible)
    {
        stopwatchNativeVisible = visible;
        Interlocked.Increment(ref stopwatchNativeRevision);
    }

    private void RetireStopwatch()
    {
        if (stopwatch is null) { return; }
        stopwatch.Retire();
        stopwatchRunAdmission = null;
        isStopwatchPanelVisible = false;
        stopwatchDisplay = "Idle. Previous run/result discarded. Observed elapsed: 00:00:00. Not a live clock.";
        uiDispatcher.Post(() =>
        {
            OnPropertyChanged(nameof(StopwatchDisplay));
            OnPropertyChanged(nameof(IsStopwatchPanelVisible));
        });
    }

    private Task RunStopwatchNative(StopwatchOperation operation)
    {
        Interlocked.Increment(ref workspaceSurfaceInputRevision);
        return HostRequestRunner.RunAsync(HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi,
            () => ExecuteStopwatchCommand(new(operation), SecurityAuditInitiator.LocalUser, native: true));
    }

    private async Task ExecuteStopwatchCommand(StopwatchCommand command,
        SecurityAuditInitiator initiator, ManualCallInput? originalInput = null, bool native = false)
    {
        // Never replace a pending question, approval, or native security review with stopwatch output.
        if (IsResponseInteractionPending || IsGrantEditorVisible || !responseReadDecisionsEligible())
        {
            Transcript = "Stopwatch blocked: resolve the current exact question or security review first. It remains unchanged.";
            return;
        }
        var origin = OriginalOrigin(initiator);
        var captured = originalInput ?? CaptureManualCallInput(origin, CallPolicyRevision);
        var owner = captured.NavigationOwnerRevision;
        var call = CallPolicyRevision;
        var name = AssistantName;
        var text = CommandText;
        var nativeRevision = Interlocked.Read(ref stopwatchNativeRevision);
        var inputRevision = Interlocked.Read(ref workspaceSurfaceInputRevision);
        var workflowRevision = stopwatch?.Revision ?? -1;
        var initiallyAdmitted = captured.NavigationInitiallyAdmitted && captured.Eligible();
        bool RunAdmitted() => !disposed && IsCallMutationHostEligible && !IsProtectedCall
            && captured.Revision == CallPolicyRevision && call == CallPolicyRevision
            && owner == auditImpactOwnerRevision();
        bool Admitted() => initiallyAdmitted && RunAdmitted() && captured.Eligible()
            && string.Equals(captured.AssistantName, name, StringComparison.Ordinal)
            && origin is RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice
            && (!native || origin == RequestOrigin.LocalUi)
            && !IsResponseInteractionPending && !IsGrantEditorVisible && responseReadDecisionsEligible()
            && string.Equals(name, AssistantName, StringComparison.Ordinal)
            && string.Equals(text, CommandText, StringComparison.Ordinal)
            && inputRevision == Interlocked.Read(ref workspaceSurfaceInputRevision)
            && (!native || stopwatchNativeAvailable && stopwatchNativeVisible
                && nativeRevision == Interlocked.Read(ref stopwatchNativeRevision))
            && (native || (!command.IsActivated || isAssistantNameAvailable)
                && (origin != RequestOrigin.ActivatedVoice || command.IsActivated));

        if (!Admitted())
        {
            RetireStopwatch();
            if (!disposed && IsHostInputEligible) { PresentStopwatch("Unavailable: original input, private owner, call or native lifetime admission was denied."); }
            return;
        }
        if (command.Operation == StopwatchOperation.Invalid)
        {
            PresentStopwatch("Command not accepted. Use exactly: " + StopwatchCommand.Syntax);
            return;
        }
        if (stopwatch is null)
        {
            PresentStopwatch("Unavailable: the local monotonic stopwatch workflow is not composed.");
            return;
        }
        if (stopwatchRunAdmission is { } previous && !previous())
        {
            RetireStopwatch();
            PresentStopwatch("Unavailable: previous ownership was retired. Initiate a fresh command.");
            return;
        }
        await HostRequestRunner.RunAsync(origin, async () =>
        {
            try
            {
                var audit = await StartAuditAsync(SecurityAuditCategory.ApplicationExecution,
                    "stopwatch." + command.Operation.ToString().ToLowerInvariant(), initiator, "stopwatch.run-local");
                var result = stopwatch.Execute(command.Operation, workflowRevision, Admitted, CancellationToken.None);
                await CompleteAuditAsync(audit, result.Outcome switch
                {
                    StopwatchOutcome.Denied => SecurityAuditOutcome.Denied,
                    StopwatchOutcome.TimeUnavailable => SecurityAuditOutcome.Failed,
                    _ => SecurityAuditOutcome.Succeeded,
                }, result.Outcome.ToString().ToLowerInvariant());
                if (!Admitted()) { RetireStopwatch(); return; }
                if (result.Snapshot is { } observed)
                {
                    stopwatchRunAdmission = RunAdmitted;
                    stopwatchDisplay = observed.State + ". Observed elapsed: "
                        + observed.ObservedElapsed.ToString("c", CultureInfo.InvariantCulture)
                        + ". Not a live clock; updates only on explicit status or stop.";
                    OnPropertyChanged(nameof(StopwatchDisplay));
                }
                else { RetireStopwatch(); }
                PresentStopwatch(result.Outcome switch
                {
                    StopwatchOutcome.Started => "Started. One run-only stopwatch; no background work or automatic refresh.",
                    StopwatchOutcome.Observed => "Status observed explicitly.",
                    StopwatchOutcome.Stopped => "Stopped. Elapsed is frozen until explicit reset.",
                    StopwatchOutcome.Reset => "Reset. Previous run/result explicitly discarded; idle, not restarted.",
                    StopwatchOutcome.Help => StopwatchCommand.Syntax + ". Start only from Idle; reset explicitly discards even a running stopwatch.",
                    StopwatchOutcome.AlreadyRunning => "Already running. Start refused; the existing run is unchanged.",
                    StopwatchOutcome.ResetRequired => "Already stopped. Start refused; reset explicitly before a new run.",
                    StopwatchOutcome.AlreadyStopped => "Already stopped. The original frozen result is unchanged; no new run was made.",
                    StopwatchOutcome.NotRunning => "Idle: no running stopwatch to stop.",
                    StopwatchOutcome.TimeUnavailable => "Time observation failed or overflowed. Run/result discarded; no elapsed result is available.",
                    _ => "Unavailable: admission changed. Run/result discarded.",
                });
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or InvalidOperationException or OperationCanceledException)
            {
                RetireStopwatch();
                ApplicationLog.Error(logger, exception, "Executing the run-only local stopwatch");
                if (!disposed && IsHostInputEligible) { PresentStopwatch("Outcome unavailable: audit or host admission failed or was cancelled. Run/result discarded."); }
            }
        });
    }

    private void PresentStopwatch(string detail)
    {
        isStopwatchPanelVisible = true;
        OnPropertyChanged(nameof(IsStopwatchPanelVisible));
        PresentResponse(AssistantState.Information, "Local visual stopwatch.", detail,
            requestWindow: false, refreshOutput: false);
        RetireCurrentResponseSpeech();
        forceVisualResponse = true;
        NotifyOutputPolicyChanged();
        if (CanRevealPrivatePresentation) { WindowActionRequested?.Invoke(this, WindowAction.Show); }
    }
}
