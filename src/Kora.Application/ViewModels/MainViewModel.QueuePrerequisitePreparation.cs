using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private ISessionQueuePrerequisitePreparePresenter? prerequisitePreparePresenter;
    private long prerequisitePrepareEpoch;
    private long prerequisiteInputRevision;

    public void BindQueuePrerequisitePreparation(ISessionQueuePrerequisitePreparePresenter? presenter)
    {
        Interlocked.Increment(ref prerequisitePrepareEpoch);
        prerequisitePreparePresenter = presenter;
    }

    internal async Task ExecuteQueuePrerequisitePreparationAsync(SessionQueuePrerequisitePrepareCommand command,
        SecurityAuditInitiator initiator, bool activationPrefixed, Func<bool>? originalAdmission = null)
    {
        if (disposed) { return; }
        if (IsResponseInteractionPending || IsGrantEditorVisible)
        {
            Transcript = "Prerequisite preparation blocked: resolve the original question or security pane explicitly first. No selection changed.";
            return;
        }
        if (command.Error is not null || command.Help)
        {
            ShowInformation(command.Help ? "Queue prerequisite preparation help." : "Queue prerequisite preparation not accepted.",
                command.Error ?? SessionQueuePrerequisitePrepareCommand.Syntax + ". " + SessionQueuePrerequisitePrepareCommand.Boundary);
            return;
        }
        var epoch = Interlocked.Increment(ref prerequisitePrepareEpoch);
        var input = Interlocked.Read(ref prerequisiteInputRevision);
        var security = Interlocked.Read(ref approvalPreparationRevision);
        var presenter = prerequisitePreparePresenter;
        var name = AssistantName;
        var origin = initiator switch
        {
            SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.LocalUser => RequestOrigin.LocalUi,
            SecurityAuditInitiator.VoiceCommand => RequestOrigin.ActivatedVoice,
            _ => RequestOrigin.HostSystem,
        };
        var host = CaptureConfigurationControlEligibility(origin);
        var original = originalAdmission ?? (static () => true);
        bool Eligible() => !disposed && original() && security == Interlocked.Read(ref approvalPreparationRevision)
            && host() && !IsProtectedCall && !IsGrantEditorVisible
            && epoch == Interlocked.Read(ref prerequisitePrepareEpoch)
            && input == Interlocked.Read(ref prerequisiteInputRevision)
            && ReferenceEquals(presenter, prerequisitePreparePresenter)
            && string.Equals(name, AssistantName, StringComparison.Ordinal)
            && (origin == RequestOrigin.LocalUi || origin == RequestOrigin.ActivatedVoice && activationPrefixed
                && CallObservation.AllowActivation);
        if (!Eligible())
        {
            ShowInformation("Queue prerequisite preparation denied.", "Use original typed input or the current activated assistant name with unchanged private host admission. No choice changed.");
            return;
        }
        if (presenter is null)
        {
            ShowInformation("Queue prerequisite preparation unavailable.", "Private native Sessions is not bound. No alternate selector or inferred prerequisite is used.");
            return;
        }
        try
        {
            var displayed = false;
            await uiDispatcher.InvokeAsync(async () =>
            {
                if (!Eligible()) { throw new OperationCanceledException("Preparation admission changed."); }
                displayed = await presenter.PrepareQueuePrerequisiteAsync(command, origin, Eligible, CancellationToken.None);
            });
            if (!Eligible()) { return; }
            ShowInformation(displayed ? "Queue prerequisite preparation opened." : "Queue prerequisite preparation not opened.",
                SessionQueuePrerequisitePrepareCommand.Boundary);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
            or UnauthorizedAccessException or OperationCanceledException or ArgumentException)
        {
            ApplicationLog.QueuePrerequisitePreparationFailed(logger, exception.GetType().Name);
            if (!Eligible()) { return; }
            ShowFailure("Queue prerequisite preparation unavailable.", "No choice changed. Restore private ownership and request fresh exact preparation.");
        }
    }
}
