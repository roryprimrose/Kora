using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private ISessionQueueReplacementReviewPresenter? queueReplacementPresenter;
    private long queueReplacementBufferRevision;
    private long queueReplacementPresenterRevision;

    public void BindSessionQueueReplacementReview(ISessionQueueReplacementReviewPresenter? presenter)
    {
        Interlocked.Increment(ref queueReplacementPresenterRevision);
        queueReplacementPresenter = presenter;
    }

    private async Task ExecuteQueueReplacementReviewAsync(SessionQueueReplacementReviewCommand command,
        string original, SecurityAuditInitiator initiator, ManualCallInput? originalInput)
    {
        if (IsResponseInteractionPending)
        {
            Transcript = "Queue replacement review blocked. Finish the existing question, approval or security preview separately; its decision and presentation are unchanged.";
            return;
        }
        if (command.Error is not null || command.IsHelp)
        {
            ShowInformation(command.IsHelp ? "Queue replacement review help." : "Queue replacement review not accepted.",
                command.Error ?? SessionQueueReplacementReviewCommand.Syntax + ". " + SessionQueueReplacementReviewCommand.Boundary);
            return;
        }
        var origin = OriginalOrigin(initiator);
        var input = originalInput ?? CaptureManualCallInput(origin, CallPolicyRevision);
        var presenter = queueReplacementPresenter;
        var presenterRevision = Interlocked.Read(ref queueReplacementPresenterRevision);
        var ownerBinding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        var revision = Interlocked.Read(ref workspaceSurfaceInputRevision);
        var text = CommandText;
        var bufferRevision = Interlocked.Read(ref queueReplacementBufferRevision);
        var name = assistantNameConfiguration.Get();
        var settings = queueConfiguration?.Get();
        bool Admitted() => !disposed && initiator is SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand
            && origin is RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice && input.Origin == origin
            && input.NavigationInitiallyAdmitted && input.Eligible() && input.Revision == CallPolicyRevision
            && input.NavigationOwnerRevision == auditImpactOwnerRevision() && IsHostInputEligible
            && ownerBinding == Interlocked.Read(ref settingsOwnershipBindingRevision)
            && CanRevealPrivatePresentation && !IsProtectedCall && isAssistantNameAvailable
            && assistantNameConfiguration.Get() == name && queueConfiguration?.Get() == settings && settings?.Available != false
            && revision == Interlocked.Read(ref workspaceSurfaceInputRevision)
            && bufferRevision == Interlocked.Read(ref queueReplacementBufferRevision)
            && string.Equals(text, CommandText, StringComparison.Ordinal)
            && ReferenceEquals(presenter, queueReplacementPresenter)
            && presenterRevision == Interlocked.Read(ref queueReplacementPresenterRevision)
            && !IsResponseInteractionPending
            && (origin != RequestOrigin.ActivatedVoice || commandRouter.IsActivationPrefixed(original, AssistantName));
        if (!Admitted() || presenter is null)
        {
            ShowInformation("Queue replacement review unavailable.",
                "Use exact original typed input or the current activated name in the owning private host. Resolve pending questions or approvals separately. No fallback or confirmation.");
            return;
        }
        var continuation = HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Presentation, origin);
        try
        {
            var nativeLifetime = presenter.CaptureLifetime();
            await uiDispatcher.InvokeAsync(async () =>
            {
                using var activity = continuation();
                try
                {
                    if (!Admitted() || !nativeLifetime()) { throw new OperationCanceledException("Review navigation revoked."); }
                    var displayed = await presenter.ReviewReplacementAsync(command, origin, Admitted, CancellationToken.None);
                    if (!Admitted()) { throw new OperationCanceledException("Review input revoked."); }
                    ShowInformation(displayed ? "Queue replacement review opened." : "Queue replacement review not opened.",
                        SessionQueueReplacementReviewCommand.Boundary);
                    activity.Complete(displayed ? HostOperationOutcome.Completed : HostOperationOutcome.Cancelled);
                }
                catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
                catch { activity.Complete(HostOperationOutcome.Failed); throw; }
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
            or UnauthorizedAccessException or OperationCanceledException or ArgumentException)
        {
            ApplicationLog.QueueReplacementReviewFailed(logger, exception.GetType().Name);
            if (Admitted())
            {
                ShowFailure("Queue replacement review unavailable.",
                    "No replacement was committed. Inspect the exact native source and request a new review explicitly; no fresh retry or alternate target.");
            }
        }
    }
}
