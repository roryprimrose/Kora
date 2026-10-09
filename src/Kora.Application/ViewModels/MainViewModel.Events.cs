using System.Text.Json;

using Kora.Application.Interaction;
using Kora.Core.Auditing;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private LocalEventBroker? localEvents;
    public void BindLocalEvents(LocalEventBroker broker) => localEvents = broker;

    internal async Task ExecuteLocalEventCommandAsync(LocalEventCommand command, SecurityAuditInitiator initiator)
    {
        if (disposed) { return; }
        if (pendingModelQuestion is not null || IsGrantChangePending || IsModelActionApprovalPending)
        {
            Transcript = "Event command blocked; the current exact question or approval remains unchanged.";
            return;
        }
        if (command.Operation == LocalEventOperation.Invalid)
        {
            PresentLocalEventResponse(LocalEventCommand.Syntax);
            return;
        }
        var origin = initiator switch
        {
            SecurityAuditInitiator.VoiceCommand => RequestOrigin.ActivatedVoice,
            SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand => RequestOrigin.LocalUi,
            _ => RequestOrigin.HostSystem,
        };
        var call = CallPolicyRevision;
        var recovery = Volatile.Read(ref voiceRecoveryRevision);
        var topology = privacyObservation.Current.TopologyRevision;
        bool Eligible() => !disposed && IsHostInputEligible && !IsProtectedCall
            && CallPolicyRevision == call && privacyObservation.Current.TopologyRevision == topology
            && pendingModelQuestion is null && !IsGrantChangePending && !IsModelActionApprovalPending
            && (origin != RequestOrigin.ActivatedVoice || IsVoiceEnabled && HasVoiceConsent
                && CallObservation.AllowActivation && privacyObservation.Current.CanCapture
                && Volatile.Read(ref voiceRecoveryRevision) == recovery);
        if (localEvents is null || origin == RequestOrigin.HostSystem || !Eligible())
        {
            PresentLocalEventResponse("Local events unavailable. Current original input, private ownership, clear call admission and an exact native host-held event are required.");
            return;
        }
        try
        {
            var result = await localEvents.ExecuteAsync(command, origin, Eligible, CancellationToken.None);
            if (!Eligible()) { return; }
            PresentLocalEventResponse(JsonSerializer.Serialize(result) + "\n" + result.DisplaySummary + "\n" + LocalEventSnapshot.Scope);
            sessionPresentationSources.Add(result.Event.SessionId.Value);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or InvalidDataException
            or UnauthorizedAccessException or OperationCanceledException)
        {
            LocalEventCommandFailed(logger, exception.GetType().Name);
            if (disposed || !IsHostInputEligible) { return; }
            PresentLocalEventResponse("Event outcome unavailable or unconfirmed. Refresh the native selected work surface under fresh admission; no retry, rollback, speech or effect is inferred. "
                + exception.GetType().Name);
        }
    }

    private void PresentLocalEventResponse(string body)
    {
        PresentResponse(Kora.Core.AssistantState.Information, "Exact local event.", body,
            requestWindow: false, refreshOutput: false);
        forceVisualResponse = true;
        NotifyOutputPolicyChanged();
    }
}
