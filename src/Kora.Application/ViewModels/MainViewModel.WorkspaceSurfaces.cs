using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Commands;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public event Func<WorkspaceSurfaceNavigation, Task>? WorkspaceSurfaceRequested;
    private long workspaceSurfaceInputRevision;

    private async Task ExecuteWorkspaceSurfaceCommand(
        WorkspaceSurfaceCommand command, string input, SecurityAuditInitiator initiator,
        ManualCallInput? originalInput)
    {
        if (command.Target == WorkspaceSurfaceTarget.Invalid)
        {
            ReportHostInteractionFailure("Navigation command not accepted. Use exactly: "
                + string.Join(" | ", WorkspaceSurfaceCommand.FixedPhrases) + ". No target or decision changed.");
            return;
        }
        if ((!isAssistantNameAvailable && commandRouter.IsActivationPrefixed(input, AssistantName))
            || (initiator == SecurityAuditInitiator.VoiceCommand
                && !commandRouter.IsActivationPrefixed(input, AssistantName)))
        {
            ReportHostInteractionFailure("Navigation unavailable: activated voice requires the current available assistant-name prefix.");
            return;
        }
        var admitted = CaptureWorkspaceSurfaceAdmission(initiator, originalInput);
        if (!admitted() || WorkspaceSurfaceRequested is not { } handler)
        {
            ReportHostInteractionFailure("Navigation unavailable: the native surface route or current private host admission is unavailable. No window or session was created.");
            return;
        }
        await HostRequestRunner.RunAsync(OriginalOrigin(initiator), () => handler(new(command.Target, admitted)));
    }

    private Func<bool> CaptureWorkspaceSurfaceAdmission(SecurityAuditInitiator initiator, ManualCallInput? originalInput)
    {
        var captured = originalInput ?? CaptureManualCallInput(OriginalOrigin(initiator), CallPolicyRevision);
        var revision = Interlocked.Read(ref workspaceSurfaceInputRevision);
        var sourceText = CommandText;
        var owner = captured.NavigationOwnerRevision;
        var initiallyAdmitted = captured.NavigationInitiallyAdmitted && captured.Eligible() && !IsProtectedCall;
        return () => initiallyAdmitted && captured.Eligible() && !IsProtectedCall
            && captured.Revision == CallPolicyRevision && owner == auditImpactOwnerRevision()
            && captured.OwnershipBindingRevision == Interlocked.Read(ref settingsOwnershipBindingRevision)
            && revision == Interlocked.Read(ref workspaceSurfaceInputRevision)
            && string.Equals(sourceText, CommandText, StringComparison.Ordinal)
            && captured.AssistantNameRevision == assistantNameConfiguration.Get().Revision;
    }
}
