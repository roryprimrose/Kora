using Kora.Application.Definitions;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private IOwnedDefinitionPreparation? ownedDefinitionPreparation;
    private long ownedDefinitionPreparationBinding;

    public void BindOwnedDefinitionPreparation(IOwnedDefinitionPreparation? preparation)
    {
        ownedDefinitionPreparation = preparation;
        Interlocked.Increment(ref ownedDefinitionPreparationBinding);
    }

    private bool OwnedPreparationReviewPending => IsResponseInteractionPending || IsGrantEditorVisible
        || FileReview is not null || FolderReview is not null || providerFieldPreview is not null;

    private void PrepareOwnedDefinition(OwnedDefinitionPreparationCommand command, string original,
        SecurityAuditInitiator initiator, ManualCallInput? originalInput)
    {
        if (OwnedPreparationReviewPending)
        {
            Transcript = "Owned metadata preparation unavailable. The pending question, approval or source review remains unchanged.";
            return;
        }
        var origin = OriginalOrigin(initiator);
        var input = originalInput ?? CaptureManualCallInput(origin, CallPolicyRevision);
        var epoch = Interlocked.Read(ref approvalPreparationRevision);
        var commandEpoch = Interlocked.Read(ref attachmentCatalogueComposerRevision);
        var routingEpoch = Interlocked.Read(ref workspaceSurfaceInputRevision);
        var source = CommandText;
        var nameRevision = assistantNameConfiguration.Get().Revision;
        var binding = Interlocked.Read(ref ownedDefinitionPreparationBinding);
        var owner = auditImpactOwnerRevision();
        var control = auditImpactControlRevision;
        var presenter = ownedDefinitionPreparation;
        bool Admitted() => !disposed && isAssistantNameAvailable
            && origin is RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice
            && initiator is SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand
            && input.Origin == origin && input.Eligible() && input.Revision == CallPolicyRevision
            && !IsProtectedCall && IsSkillReviewCallEligible && CanRevealPrivatePresentation && !OwnedPreparationReviewPending
            && owner == auditImpactOwnerRevision() && control == auditImpactControlRevision
            && epoch == Interlocked.Read(ref approvalPreparationRevision)
            && commandEpoch == Interlocked.Read(ref attachmentCatalogueComposerRevision)
            && routingEpoch == Interlocked.Read(ref workspaceSurfaceInputRevision)
            && binding == Interlocked.Read(ref ownedDefinitionPreparationBinding)
            && string.Equals(source, CommandText, StringComparison.Ordinal)
            && nameRevision == assistantNameConfiguration.Get().Revision
            && (origin != RequestOrigin.ActivatedVoice || commandRouter.IsActivationPrefixed(original, AssistantName)
                && voiceOwnershipEligible());
        if (!command.IsValid || !Admitted())
        { PresentOwnedPreparation(false); return; }
        if (command.IsHelp)
        { PresentOwnedPreparation(false, help: true); return; }
        if (presenter is null)
        { PresentOwnedPreparation(false); return; }
        Func<bool> native;
        try { native = presenter.CaptureLifetime(); }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or IOException
            or UnauthorizedAccessException or OperationCanceledException)
        {
            OwnedDefinitionPreparationFailed(logger, exception.GetType().Name);
            PresentOwnedPreparation(false);
            return;
        }
        var continuation = HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Presentation, origin);
        uiDispatcher.Post(() =>
        {
            using var activity = continuation();
            var handoffActive = true;
            try
            {
                var prepared = Admitted() && native()
                    && presenter.TryPrepare(command.Kind!.Value, command.OwnedId!, () => handoffActive && Admitted()) && Admitted();
                PresentOwnedPreparation(prepared);
                activity.Complete(prepared ? HostOperationOutcome.Completed : HostOperationOutcome.Cancelled);
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or IOException
                or UnauthorizedAccessException or OperationCanceledException)
            {
                OwnedDefinitionPreparationFailed(logger, exception.GetType().Name);
                PresentOwnedPreparation(false);
                activity.Complete(HostOperationOutcome.Failed);
            }
            finally { handoffActive = false; }
        });
    }

    private void PresentOwnedPreparation(bool prepared, bool help = false)
    {
        if (disposed || !IsHostInputEligible || OwnedPreparationReviewPending) { return; }
        PresentResponse(Kora.Core.AssistantState.Information,
            help ? "Owned definition metadata help" : prepared ? "Owned metadata PREPARED / REVIEW NEEDED"
                : "Owned metadata preparation unavailable / REVIEW NEEDED",
            OwnedDefinitionPreparationCommand.Syntax, requestWindow: false, refreshOutput: false);
        forceVisualResponse = true;
        NotifyOutputPolicyChanged();
    }
}
