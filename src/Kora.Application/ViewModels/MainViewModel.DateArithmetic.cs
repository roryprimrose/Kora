using Kora.Application.Calendar;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly DateArithmeticRegistry? dateArithmetic;

    private void ExecuteDateArithmetic(DateArithmeticCommand command, string original,
        SecurityAuditInitiator initiator, ManualCallInput? originalInput)
    {
        var origin = OriginalOrigin(initiator);
        var input = originalInput ?? CaptureManualCallInput(origin, CallPolicyRevision);
        if (!isAssistantNameAvailable || HostActivity.HasScope && HostActivity.Current is null
            || input.Origin != origin || !input.Eligible()
            || input.Revision != CallPolicyRevision || !CanRevealPrivatePresentation || IsProtectedCall
            || origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || initiator is not (SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || origin == RequestOrigin.ActivatedVoice && !commandRouter.IsActivationPrefixed(original, AssistantName))
        {
            Transcript = "Date arithmetic unavailable: use original typed input or the current assistant-name voice prefix in the owning unlocked host.";
            return;
        }
        if (IsResponseInteractionPending || HasResponseActions)
        {
            Transcript = "Date arithmetic was not run. Finish or cancel the pending question, approval or preview first.";
            return;
        }
        if (command.Error is { } error)
        {
            Transcript = "Date arithmetic input invalid. " + error;
            return;
        }
        if (dateArithmetic is null)
        {
            Transcript = "Date arithmetic unavailable: host actions are not registered.";
            return;
        }
        HostRequestRunner.Run(origin, () =>
        {
            try
            {
                var result = dateArithmetic.Invoke(command, CancellationToken.None);
                ShowInformation("Calendar date arithmetic", result);
            }
            catch (InvalidDataException exception)
            {
                Transcript = "Date arithmetic failed. " + exception.Message;
            }
        });
    }
}
