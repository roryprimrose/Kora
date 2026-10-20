using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Conversions;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly Kora.Tools.Conversions.ConvertUnits convertUnits = new();

    private void ExecuteConvertCommand(ConvertCommand command, SecurityAuditInitiator initiator, ManualCallInput? original)
    {
        var origin = OriginalOrigin(initiator);
        var input = original ?? CaptureManualCallInput(origin, CallPolicyRevision);
        bool Admitted() => !disposed && input.Origin == origin && input.Eligible()
            && input.NavigationInitiallyAdmitted && input.NavigationOwnerRevision == auditImpactOwnerRevision()
            && input.Revision == CallPolicyRevision && !IsProtectedCall && CanRevealPrivatePresentation
            && isAssistantNameAvailable && origin is RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice
            && (origin != RequestOrigin.ActivatedVoice || command.IsActivated && voiceOwnershipEligible());
        if (!Admitted())
        {
            ConversionFinished(ConversionOutcome.Denied);
            Transcript = "Unit conversion unavailable: original private host, ownership, call or current-name admission is closed.";
            return;
        }
        if (IsResponseInteractionPending || IsGrantEditorVisible || IsBusy)
        {
            ConversionFinished(ConversionOutcome.Denied);
            Transcript = "Unit conversion unavailable during a pending question, approval, security preview or operation. Nothing was answered or approved.";
            return;
        }
        HostRequestRunner.Run(origin, () =>
        {
            if (command.IsHelp)
            {
                ShowInformation("Local unit conversion", ConvertCommand.Syntax);
                return;
            }
            var result = convertUnits.Execute(command, Admitted, CancellationToken.None);
            ConversionFinished(result.Outcome);
            if (result.Outcome == ConversionOutcome.Succeeded)
            {
                ShowInformation("Unit conversion result",
                    $"{result.Value} {command.To!.Value.ToString().ToLowerInvariant()}. "
                    + (result.Rounded ? "Rounded. " : "Exact. ") + UnitConversion.Precision);
            }
            else { ShowFailure("Unit conversion " + result.Outcome, "No result. " + ConvertCommand.Syntax); }
        });
    }
}
