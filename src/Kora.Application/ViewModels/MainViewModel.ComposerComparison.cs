using Kora.Application.Diagnostics;
using Kora.Application.Presentation;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private const string ComposerComparisonUnavailable = "Compare unavailable. Use the same selected native Sessions composer and explicitly read exact saved revision first; nothing was read or changed.";
    private IComposerComparisonNavigation? composerComparisonNavigation;
    private long composerComparisonInputRevision;

    public void BindComposerComparisonNavigation(IComposerComparisonNavigation? navigation) =>
        composerComparisonNavigation = navigation;

    private async Task ExecuteComposerComparisonCommand(SessionDraftCompareCommand command, string original,
        SecurityAuditInitiator initiator, ManualCallInput? originalInput)
    {
        const string unavailable = ComposerComparisonUnavailable;
        var origin = OriginalOrigin(initiator);
        var input = originalInput ?? CaptureManualCallInput(origin, CallPolicyRevision);
        var presenter = composerComparisonNavigation;
        var revision = Interlocked.Read(ref composerComparisonInputRevision);
        var text = CommandText;
        var nameRevision = assistantNameConfiguration.Get().Revision;
        var binding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        var initial = input.NavigationInitiallyAdmitted && input.Eligible() && !IsProtectedCall
            && isAssistantNameAvailable
            && origin is RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice
            && initiator is SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand
            && (origin != RequestOrigin.ActivatedVoice || commandRouter.IsActivationPrefixed(original, AssistantName)
                && SessionDraftCompareCommand.Parse(Kora.Core.Artifacts.ArtifactCommandRouter.RemoveActivationPrefix(
                    original, AssistantName), AssistantName) is not null);
        bool Admitted() => initial && input.Origin == origin && input.Eligible() && !IsProtectedCall
            && input.Revision == CallPolicyRevision && input.NavigationOwnerRevision == auditImpactOwnerRevision()
            && CanRevealPrivatePresentation && binding == Interlocked.Read(ref settingsOwnershipBindingRevision)
            && revision == Interlocked.Read(ref composerComparisonInputRevision)
            && string.Equals(text, CommandText, StringComparison.Ordinal)
            && nameRevision == assistantNameConfiguration.Get().Revision
            && ReferenceEquals(presenter, composerComparisonNavigation);
        if (!Admitted()) { Transcript = unavailable; return; }
        if (command.Help)
        {
            Transcript = SessionDraftCompareCommand.Syntax + ". " + SessionDraftCompareCommand.Boundary;
            return;
        }
        if (command.SessionId is not { } id)
        {
            Transcript = "Compare command not accepted. " + SessionDraftCompareCommand.Syntax + ". " + SessionDraftCompareCommand.Boundary;
            return;
        }
        try
        {
            // Capture the native source before any route feedback can cause a reentrant source change.
            var launch = presenter?.Capture(new(id), origin, Admitted);
            if (launch is null || !Admitted() || !launch.IsCurrent()) { Transcript = unavailable; return; }
            await uiDispatcher.InvokeAsync(async () =>
            {
                if (!Admitted() || !launch.IsCurrent()) { Transcript = unavailable; return; }
                var opened = await launch.Open();
                Transcript = opened && Admitted() && launch.IsCurrent()
                    ? "Exact observed draft comparison opened in the native view only. Nothing was read, saved or submitted."
                    : unavailable;
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
            or UnauthorizedAccessException or OperationCanceledException or ArgumentException)
        {
            ApplicationLog.Error(logger, exception, "Opening native composer comparison");
            Transcript = unavailable;
        }
    }
}
