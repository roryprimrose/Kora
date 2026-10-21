using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private ISessionQueueCancellationReviewPresenter? queueCancellationPresenter;
    private long queueCancellationPreparationEpoch;

    public void BindQueueCancellationReview(ISessionQueueCancellationReviewPresenter? presenter)
    {
        Interlocked.Increment(ref queueCancellationPreparationEpoch);
        queueCancellationPresenter = presenter;
    }

    internal async Task ExecuteQueueCancellationPreparationAsync(SessionQueueCancellationReviewCommand command,
        SecurityAuditInitiator initiator, bool activationPrefixed)
    {
        if (disposed) { return; }
        var epoch = Interlocked.Increment(ref queueCancellationPreparationEpoch);
        if (command.Error is not null || command.IsHelp)
        {
            ShowInformation(command.IsHelp ? "Queue cancellation preparation help." : "Queue cancellation preparation not accepted.",
                command.Error ?? SessionQueueCancellationReviewCommand.Syntax + ". " + SessionQueueCancellationReviewCommand.Boundary);
            return;
        }
        if (pendingModelQuestion is not null || IsGrantChangePending || IsModelActionApprovalPending)
        {
            ShowInformation("Queue cancellation preparation blocked.", "Resolve the current question or approval explicitly first. No target or decision changed.");
            return;
        }
        var origin = initiator switch
        {
            SecurityAuditInitiator.VoiceCommand => RequestOrigin.ActivatedVoice,
            SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand => RequestOrigin.LocalUi,
            _ => RequestOrigin.HostSystem,
        };
        var presenter = queueCancellationPresenter;
        var call = CallPolicyRevision;
        var privacy = privacyObservation.Current.TopologyRevision;
        var recovery = Volatile.Read(ref voiceRecoveryRevision);
        var owner = auditImpactOwnerRevision();
        var binding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        var input = Interlocked.Read(ref workspaceSurfaceInputRevision);
        var voiceGeneration = voiceRecognition.Generation;
        var name = AssistantName;
        bool Eligible() => !disposed && epoch == Interlocked.Read(ref queueCancellationPreparationEpoch)
            && ReferenceEquals(presenter, queueCancellationPresenter) && IsCallMutationHostEligible && !IsProtectedCall
            && CallPolicyRevision == call && privacyObservation.Current.TopologyRevision == privacy
            && auditImpactOwnerRevision() == owner
            && Interlocked.Read(ref settingsOwnershipBindingRevision) == binding
            && Interlocked.Read(ref workspaceSurfaceInputRevision) == input
            && pendingModelQuestion is null && !IsGrantChangePending && !IsModelActionApprovalPending
            && (origin == RequestOrigin.LocalUi || origin == RequestOrigin.ActivatedVoice
                && activationPrefixed && IsVoiceEnabled && HasVoiceConsent && CallObservation.AllowActivation
                && recovery == Volatile.Read(ref voiceRecoveryRevision)
                && voiceRecognition.Generation == voiceGeneration
                && string.Equals(name, AssistantName, StringComparison.Ordinal));
        if (!Eligible())
        {
            ShowInformation("Queue cancellation preparation denied.", "Use original typed input or the current activated assistant name with unchanged private host admission.");
            return;
        }
        if (presenter is null)
        {
            ShowInformation("Queue cancellation preparation unavailable.", "The private native Sessions review is not bound. No alternate session or model fallback is used.");
            return;
        }
        using var activity = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Presentation, origin);
        try
        {
            var displayed = await presenter.PrepareQueueCancellationAsync(command, origin, Eligible, CancellationToken.None);
            if (!Eligible()) { activity.Complete(HostOperationOutcome.Cancelled); return; }
            activity.Complete(displayed ? HostOperationOutcome.Completed : HostOperationOutcome.Cancelled);
            ShowInformation(displayed ? "Queue cancellation review prepared." : "Queue cancellation review not opened.",
                SessionQueueCancellationReviewCommand.Boundary + " If cancelled, unavailable or stale, request a fresh exact review.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
            or UnauthorizedAccessException or OperationCanceledException or ArgumentException)
        {
            activity.Complete(exception is OperationCanceledException ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
            ApplicationLog.QueueCancellationPreparationFailed(logger, exception.GetType().Name);
            if (!Eligible()) { return; }
            ShowFailure("Queue cancellation preparation unavailable.", "No pending work changed. Restore private ownership, inspect the exact IDs and request a fresh review.");
        }
    }
}
