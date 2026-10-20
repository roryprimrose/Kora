using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private ISessionQueueMoveReviewPresenter? queueMoveReviewPresenter;
    private long queueMoveReviewEpoch;
    private long queueMoveInputRevision;

    public void BindSessionQueueMoveReview(ISessionQueueMoveReviewPresenter? presenter)
    {
        Interlocked.Increment(ref queueMoveReviewEpoch);
        queueMoveReviewPresenter = presenter;
    }

    private async Task ExecuteQueueMoveReviewAsync(SessionQueueMoveReviewCommand command, string input,
        SecurityAuditInitiator initiator, ManualCallInput? originalInput)
    {
        var epoch = Interlocked.Increment(ref queueMoveReviewEpoch);
        if (IsResponseInteractionPending)
        {
            Transcript = "Queue move review blocked. Resolve the original question or approval explicitly; no decision changed.";
            return;
        }
        if (command.Error is not null)
        {
            Transcript = "Queue move review not accepted. " + command.Error;
            return;
        }
        var origin = OriginalOrigin(initiator);
        var captured = originalInput ?? CaptureManualCallInput(origin, CallPolicyRevision);
        var presenter = queueMoveReviewPresenter;
        var revision = Interlocked.Read(ref workspaceSurfaceInputRevision);
        var inputRevision = Interlocked.Read(ref queueMoveInputRevision);
        var ownerBinding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        var nameRevision = assistantNameConfiguration.Get().Revision;
        var name = AssistantName;
        var control = CaptureConfigurationControlEligibility(origin);
        var activated = commandRouter.IsActivationPrefixed(input, name);
        bool Eligible() => !disposed && epoch == Interlocked.Read(ref queueMoveReviewEpoch)
            && ReferenceEquals(presenter, queueMoveReviewPresenter) && captured.NavigationInitiallyAdmitted
            && captured.Eligible() && control() && !IsProtectedCall
            && captured.NavigationOwnerRevision == auditImpactOwnerRevision()
            && ownerBinding == Interlocked.Read(ref settingsOwnershipBindingRevision)
            && nameRevision == assistantNameConfiguration.Get().Revision
            && inputRevision == Interlocked.Read(ref queueMoveInputRevision)
            && revision == Interlocked.Read(ref workspaceSurfaceInputRevision)
            && (origin == RequestOrigin.LocalUi || origin == RequestOrigin.ActivatedVoice
                && activated && isAssistantNameAvailable && CallObservation.AllowActivation);
        if (!Eligible() || presenter is null)
        {
            Transcript = "Queue move review unavailable. Use original typed input or the current activated assistant name under unchanged private ownership. Native preparation is required; no fallback or move occurred.";
            return;
        }
        try
        {
            var displayed = false;
            var navigation = presenter.CaptureNavigationEligibility();
            await uiDispatcher.InvokeAsync(async () =>
            {
                if (!Eligible() || !navigation()) { throw new OperationCanceledException("Original queue review input or navigation retired."); }
                displayed = await presenter.ReviewQueueMoveAsync(command, origin, Eligible, CancellationToken.None);
            });
            if (!Eligible()) { return; }
            Transcript = (displayed ? "Queue move review prepared. " : "Queue move review not prepared. ")
                + SessionQueueMoveReviewCommand.Boundary;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
            or UnauthorizedAccessException or OperationCanceledException or ArgumentException)
        {
            ApplicationLog.QueueMoveReviewFailed(logger, exception.GetType().Name);
            if (!Eligible()) { return; }
            Transcript = "Queue move review unavailable. No move was requested. Inspect the exact current pair and request a fresh command; no retry or different neighbor is used.";
        }
    }
}
