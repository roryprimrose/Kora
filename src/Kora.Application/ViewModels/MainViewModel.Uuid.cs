using Kora.Application.Tools;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Tools.Utilities;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private readonly UuidGenerate uuidGenerate;
    private readonly UuidInspect uuidInspect;
    private readonly UuidHelp uuidHelp;

    private async Task ExecuteUuidCommandAsync(UuidCommand command,
        SecurityAuditInitiator initiator, ManualCallInput? originalInput)
    {
        var origin = OriginalOrigin(initiator);
        var input = originalInput ?? CaptureManualCallInput(origin, CallPolicyRevision);
        if (command.Operation == UuidOperation.Invalid)
        {
            Transcript = "UUID command not accepted. " + UuidHelp.Syntax;
            return;
        }
        bool Admitted() => origin is RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice
            && initiator is SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand
            && isAssistantNameAvailable
            && (origin != RequestOrigin.ActivatedVoice || command.ActivationPrefixed)
            && input.NavigationInitiallyAdmitted && input.Eligible() && CanRevealPrivatePresentation && !IsProtectedCall
            && input.Revision == CallPolicyRevision && input.NavigationOwnerRevision == auditImpactOwnerRevision()
            && !IsResponseInteractionPending && !IsGrantEditorVisible && !IsBusy
            && auditImpactPreview?.Get().ProposedDays is null
            && diagnosticImpactPreview?.Get().ProposedDays is null
            && ProviderFieldConfirmationId is null && sessionProviderProposal is null
            && pendingPowerAction is null;
        if (!Admitted())
        {
            Transcript = "UUID utility unavailable: original private owning-host input is required, with no protected call or pending question, approval or preview. Nothing was generated or looked up.";
            return;
        }
        using var activity = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Tool, origin);
        try
        {
            var result = command.Operation switch
            {
                UuidOperation.Generate => uuidGenerate.Execute(CancellationToken.None),
                UuidOperation.Inspect => uuidInspect.Execute(command.Canonical!),
                UuidOperation.Help => uuidHelp.Execute(),
                _ => throw new InvalidOperationException("Unknown UUID operation."),
            };
            if (!Admitted()) { throw new InvalidOperationException("UUID utility admission changed before presentation."); }
            UuidResult(logger, "completed");
            ShowInformation("UUID format utility.", result);
            await SpeakCurrentResponseAsync();
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException
            or OperationCanceledException or System.Security.Cryptography.CryptographicException)
        {
            UuidResult(logger, "failed");
            activity.Complete(exception is OperationCanceledException
                ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
            ShowFailure("UUID utility failed.", "No UUID result was accepted. Retry the exact command from the owning host; no fallback or retry was performed.");
        }
    }
}
