using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using AllWorkStopOutcome = Kora.Application.Hosting.AllWorkStopResult.OutcomeKind;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private Func<RequestOrigin, Func<bool>, CancellationToken, Task<AllWorkStopResult>>? allWorkStop;
    private bool allWorkStopRunning;
    private long allWorkStopPresentationRevision;

    public AllWorkStopResult? LastAllWorkStop { get; private set; }

    public void BindAllWorkStop(Func<RequestOrigin, Func<bool>, CancellationToken, Task<AllWorkStopResult>>? stop)
    {
        PrivacyClosureRequested -= RetireAllWorkStopPresentation;
        allWorkStop = stop;
        RetireAllWorkStopPresentation(this, EventArgs.Empty);
        if (stop is not null) { PrivacyClosureRequested += RetireAllWorkStopPresentation; }
    }

    private void RetireAllWorkStopPresentation(object? sender, EventArgs args)
    {
        Interlocked.Increment(ref allWorkStopPresentationRevision);
        LastAllWorkStop = null;
    }

    private async Task<bool> TryRouteStopAllWorkAsync(string text, float confidence,
        SecurityAuditInitiator initiator, ManualCallInput? originalInput)
    {
        if (commandRouter.Match(text, AssistantName).Command is not { Action: BuiltInAction.StopAllWork } command)
        { return false; }
        BeginTranscriptPresentation(text, confidence, initiator);
        await ExecuteAsync(command, initiator, sourceCurrent: originalInput?.Eligible);
        return true;
    }

    public async Task<AllWorkStopResult> StopAllWorkAsync(Func<bool>? sourceCurrent = null, CancellationToken token = default)
    {
        if (allWorkStopRunning) { return new(AllWorkStopOutcome.InProgress); }
        var stop = allWorkStop;
        var presentation = Interlocked.Read(ref allWorkStopPresentationRevision);
        var origin = HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi;
        var privacy = privacyObservation.Current.TopologyRevision;
        var call = CallPolicyRevision;
        var binding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        var owner = settingsOwnershipRevision();
        var input = CaptureManualCallInput(origin, call);
        var sourceText = CommandText;
        bool Current() => !disposed && !token.IsCancellationRequested && IsHostInputEligible
            && IsCallMutationHostEligible && CanRevealPrivatePresentation
            && privacyObservation.Current.TopologyRevision == privacy && CallPolicyRevision == call
            && Interlocked.Read(ref settingsOwnershipBindingRevision) == binding && settingsOwnershipRevision() == owner
            && input.Eligible() && input.InputRevision == Interlocked.Read(ref workspaceSurfaceInputRevision)
            && input.AssistantNameRevision == assistantNameConfiguration.Get().Revision
            && string.Equals(sourceText, CommandText, StringComparison.Ordinal)
            && presentation == Interlocked.Read(ref allWorkStopPresentationRevision)
            && ReferenceEquals(allWorkStop, stop) && (sourceCurrent?.Invoke() ?? true);
        if (stop is null || origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice) || !Current())
        {
            LastAllWorkStop = new(AllWorkStopOutcome.Unavailable);
            ShowInformation("Stop all work unavailable.", LastAllWorkStop.Summary);
            return LastAllWorkStop;
        }
        allWorkStopRunning = true;
        try
        {
            ShowInformation("Reviewing all submitted work.",
                "A complete host review and separate original-channel confirmation are required. Unsent input, editors, history and grants remain unchanged.");
            var result = await stop(origin, Current, token);
            if (presentation == Interlocked.Read(ref allWorkStopPresentationRevision) && CanRevealPrivatePresentation)
            { LastAllWorkStop = result; }
            ShowInformation("Stop all work: " + result.Outcome, result.Summary);
            return result;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReportAllWorkStopFailure(exception);
            var result = new AllWorkStopResult(AllWorkStopOutcome.PartialUnconfirmed);
            if (presentation == Interlocked.Read(ref allWorkStopPresentationRevision) && CanRevealPrivatePresentation)
            { LastAllWorkStop = result; }
            ShowFailure("Stop all work unconfirmed.", result.Summary);
            return result;
        }
        finally { allWorkStopRunning = false; }
    }

    public void PresentAllWorkJoining() => ShowInformation("Stopping reviewed submitted work.",
        "Waiting for actual provider, question, voice, worker and finite-read resources and retained terminal readback. A cancellation request is not completion. Kora stays open.");

    public void ReportAllWorkStopFailure(Exception exception) =>
        ApplicationLog.Error(logger, exception, "Reviewing or joining Stop All submitted work");
}
