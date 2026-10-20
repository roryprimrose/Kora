using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Calculations;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly Kora.Tools.Calculations.Calculate calculate = new();

    private void ExecuteCalculateCommand(CalculateCommand command, SecurityAuditInitiator initiator, ManualCallInput? original)
    {
        var origin = OriginalOrigin(initiator);
        var input = original ?? CaptureManualCallInput(origin, CallPolicyRevision);
        bool Admitted() => !disposed && input.Origin == origin && input.Eligible()
            && !IsProtectedCall && CanRevealPrivatePresentation && isAssistantNameAvailable
            && origin is RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice
            && (origin != RequestOrigin.ActivatedVoice || command.IsActivated && voiceOwnershipEligible());
        if (!Admitted())
        {
            Transcript = "Calculator unavailable: original private host, ownership, call or current-name input admission is closed.";
            return;
        }
        if (IsResponseInteractionPending || IsBusy)
        {
            Transcript = "Calculator unavailable during a pending question, approval or operation. Nothing was answered or approved.";
            return;
        }
        if (command.IsHelp)
        {
            Transcript = CalculateCommand.Syntax;
            return;
        }
        if (command.Expression is null)
        {
            Transcript = "Calculator InvalidExpression. " + CalculateCommand.Syntax;
            return;
        }
        HostRequestRunner.Run(origin, () =>
        {
            var result = calculate.Execute(command.Expression, Admitted, CancellationToken.None);
            CalculatorFinished(result.Outcome);
            // Native/transcript-only: do not replace a response source or automatically speak private arithmetic.
            Transcript = result.Outcome == CalculationOutcome.Succeeded
                ? "Calculator result: " + result.Value
                : "Calculator " + result.Outcome + ". No result. " + CalculateCommand.Syntax;
        });
    }
}
