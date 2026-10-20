using Kora.Application.Hosting;
using Kora.Application.Numbers;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Numbers;
using Kora.Tools.Numbers;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly IntegerBaseConvert integerBaseConvert;

    private void ExecuteNumberCommand(NumberCommand command, SecurityAuditInitiator initiator, ManualCallInput? originalInput)
    {
        var origin = OriginalOrigin(initiator);
        var input = originalInput ?? CaptureManualCallInput(origin, CallPolicyRevision);
        if (!isAssistantNameAvailable || !input.Eligible() || input.Origin != origin
            || input.Revision != CallPolicyRevision || input.NavigationOwnerRevision != auditImpactOwnerRevision()
            || origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || initiator != (origin == RequestOrigin.LocalUi ? SecurityAuditInitiator.TypedCommand : SecurityAuditInitiator.VoiceCommand)
            || origin == RequestOrigin.ActivatedVoice && (!command.IsActivated || !voiceOwnershipEligible())
            || IsProtectedCall || IsResponseInteractionPending || IsGrantEditorVisible || IsBusy)
        {
            Transcript = "Number conversion unavailable: original input, owning private host and no pending question, approval or protected call are required.";
            return;
        }
        HostRequestRunner.Run(origin, () =>
        {
            using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Tool);
            if (command.Error is { } error)
            {
                ShowFailure("Invalid number command.", error);
                activity.Complete(HostOperationOutcome.Failed);
                return;
            }
            if (command.IsHelp)
            {
                ShowInformation("Integer base conversion.", NumberCommand.Help);
                activity.Complete(HostOperationOutcome.Completed);
                return;
            }
            var result = integerBaseConvert.Execute(command.Request!);
            NumberConversionFinished(result.Outcome);
            if (result.Outcome == IntegerConversionOutcome.Succeeded)
            {
                ShowInformation("Integer base conversion.", result.Representation!);
                activity.Complete(HostOperationOutcome.Completed);
            }
            else
            {
                ShowFailure("Number conversion failed.", result.Outcome == IntegerConversionOutcome.Overflow
                    ? "Integer overflow: value is outside signed Int64 (-9223372036854775808 through 9223372036854775807)."
                    : "Invalid integer: use canonical ASCII digits for the declared base and optional minus; no leading zeros or -0.");
                activity.Complete(HostOperationOutcome.Failed);
            }
        });
    }
}
