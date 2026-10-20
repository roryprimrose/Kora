using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private ISessionNamePreparationPresenter? namePreparationPresenter;
    private long namePreparationEpoch;

    public void BindSessionNamePreparation(ISessionNamePreparationPresenter? presenter)
    {
        Interlocked.Increment(ref namePreparationEpoch);
        namePreparationPresenter = presenter;
    }

    private async Task ExecuteSessionNamePreparationAsync(SessionNamePreparationCommand command,
        string input, SecurityAuditInitiator initiator, ManualCallInput? originalInput)
    {
        var epoch = Interlocked.Increment(ref namePreparationEpoch);
        if (!command.IsValid || command.IsHelp)
        {
            PresentNamePreparation(command.IsHelp ? "Session name preparation help." : "Session name preparation not accepted.",
                SessionNamePreparationCommand.Syntax + ". " + SessionNamePreparationCommand.Boundary);
            return;
        }
        var origin = OriginalOrigin(initiator);
        var captured = originalInput ?? CaptureManualCallInput(origin, CallPolicyRevision);
        var presenter = namePreparationPresenter;
        var inputRevision = Interlocked.Read(ref workspaceSurfaceInputRevision);
        var sourceText = CommandText;
        var name = AssistantName;
        var nameRevision = assistantNameConfiguration.Get().Revision;
        var bufferRevision = Interlocked.Read(ref attachmentCatalogueComposerRevision);
        var initiallyAdmitted = initiator is SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand
            && captured.NavigationInitiallyAdmitted && captured.Eligible() && !IsProtectedCall
            && captured.AssistantNameRevision == nameRevision && captured.NavigationBufferRevision == bufferRevision
            && (originalInput is null || captured.InputRevision + 1 == inputRevision);
        bool Admitted() => !disposed && initiallyAdmitted && captured.Eligible() && !IsProtectedCall
            && captured.Revision == CallPolicyRevision && captured.NavigationOwnerRevision == auditImpactOwnerRevision()
            && epoch == Interlocked.Read(ref namePreparationEpoch)
            && inputRevision == Interlocked.Read(ref workspaceSurfaceInputRevision)
            && bufferRevision == Interlocked.Read(ref attachmentCatalogueComposerRevision)
            && assistantNameConfiguration.Get().Revision == nameRevision
            && ReferenceEquals(presenter, namePreparationPresenter)
            && string.Equals(sourceText, CommandText, StringComparison.Ordinal)
            && isAssistantNameAvailable && string.Equals(name, AssistantName, StringComparison.Ordinal)
            && pendingModelQuestion is null && !IsGrantChangePending && !IsModelActionApprovalPending
            && (origin == Kora.Core.Hosting.RequestOrigin.LocalUi
                || origin == Kora.Core.Hosting.RequestOrigin.ActivatedVoice
                && commandRouter.IsActivationPrefixed(input, name) && IsVoiceEnabled && HasVoiceConsent);
        if (!Admitted() || presenter is null)
        {
            PresentNamePreparation("Session name preparation unavailable.",
                "Use original typed or current-name activated input with unchanged private ownership and no pending question or approval. Nothing was renamed.");
            return;
        }
        try
        {
            await HostRequestRunner.RunAsync(origin, async () =>
            {
                var displayed = false;
                await uiDispatcher.InvokeAsync(async () =>
                {
                    if (!Admitted()) { return; }
                    displayed = await presenter.PrepareNameAsync(command, origin, Admitted, CancellationToken.None);
                });
                if (!Admitted()) { return; }
                PresentNamePreparation(displayed ? "Session name PREPARED / REVIEW NEEDED" : "Session name preparation not opened.",
                    SessionNamePreparationCommand.Boundary);
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
            or UnauthorizedAccessException or OperationCanceledException or ArgumentException)
        {
            ApplicationLog.SessionNamePreparationFailed(logger, exception.GetType().Name);
            if (Admitted())
            {
                PresentNamePreparation("Session name preparation unavailable.",
                    "No rename is confirmed. Restore private native ownership and request a fresh exact review.");
            }
        }

    }

    private void PresentNamePreparation(string title, string message) => Transcript = title + " " + message;
}
