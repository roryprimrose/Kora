using Kora.Application.Hosting;
using Kora.Application.Interaction;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private CountdownWorkflow? countdown;
    public event EventHandler? CountdownRequested;

    public void BindCountdown(CountdownWorkflow? workflow)
    {
        countdown?.Dispose();
        countdown = workflow;
    }

    public bool CanRunCountdown => !disposed && IsCallMutationHostEligible && !IsProtectedCall;

    public void ExecuteNativeCountdown(CountdownOperation operation)
    {
        if (operation is not (CountdownOperation.Status or CountdownOperation.Cancel))
        { throw new ArgumentOutOfRangeException(nameof(operation)); }
        HostRequestRunner.Run(RequestOrigin.LocalUi, () =>
        {
            if (!CanRunCountdown || countdown is null || CountdownRequested is null)
            {
                Transcript = "Timer unavailable: current private host ownership and clear call admission are required.";
                return;
            }
            Transcript = countdown.Execute(new(operation), () => CanRunCountdown);
        });
    }

    private void ExecuteCountdownCommand(CountdownCommand command,
        SecurityAuditInitiator initiator, ManualCallInput? originalInput)
    {
        var origin = OriginalOrigin(initiator);
        var input = originalInput ?? CaptureManualCallInput(origin, CallPolicyRevision);
        if (origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || !isAssistantNameAvailable
            || initiator is not (SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || origin == RequestOrigin.ActivatedVoice && !command.IsActivated
            || !input.NavigationInitiallyAdmitted || !input.Eligible() || !CanRunCountdown
            || input.Revision != CallPolicyRevision || input.NavigationOwnerRevision != auditImpactOwnerRevision()
            || input.CountdownOwnerBindingRevision != Interlocked.Read(ref settingsOwnershipBindingRevision)
            || !string.Equals(input.CountdownAssistantName, AssistantName, StringComparison.Ordinal)
            || countdown is null || CountdownRequested is null)
        {
            Transcript = "Timer unavailable: original typed or current-name activated input, private host ownership and a live native presenter are required.";
            return;
        }
        var owner = auditImpactOwnerRevision();
        var binding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        var privacy = privacyObservation.Current.TopologyRevision;
        var call = CallPolicyRevision;
        bool LifetimeEligible() => CanRunCountdown && owner == auditImpactOwnerRevision()
            && binding == Interlocked.Read(ref settingsOwnershipBindingRevision)
            && privacy == privacyObservation.Current.TopologyRevision && call == CallPolicyRevision;
        HostRequestRunner.Run(origin, () =>
        {
            Transcript = countdown.Execute(command, LifetimeEligible);
            if (command.Operation is CountdownOperation.Start or CountdownOperation.Status)
            { CountdownRequested?.Invoke(this, EventArgs.Empty); }
        });
    }
}
