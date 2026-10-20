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
        var admitted = CaptureWorkspaceSurfaceAdmission(input, initiator, originalInput);
        if (admitted is null) { return; }
        if (!admitted() || WorkspaceSurfaceRequested is not { } handler)
        {
            ReportHostInteractionFailure("Navigation unavailable: the native surface route or current private host admission is unavailable. No window or session was created.");
            return;
        }
        await HostRequestRunner.RunAsync(OriginalOrigin(initiator), () => handler(new(command.Target, admitted)));
    }

    private Func<bool>? CaptureWorkspaceSurfaceAdmission(
        string input, SecurityAuditInitiator initiator, ManualCallInput? originalInput, bool preserveResponse = false,
        bool? activated = null)
    {
        var activationPrefixed = activated ?? commandRouter.IsActivationPrefixed(input, AssistantName);
        if ((!isAssistantNameAvailable && activationPrefixed)
            || (initiator == SecurityAuditInitiator.VoiceCommand
                && !activationPrefixed))
        {
            const string message = "Navigation unavailable: activated voice requires the current available assistant-name prefix.";
            if (preserveResponse) { Transcript = message; }
            else { ReportHostInteractionFailure(message); }
            return null;
        }
        var origin = OriginalOrigin(initiator);
        var captured = originalInput ?? CaptureManualCallInput(origin, CallPolicyRevision);
        var revision = Interlocked.Read(ref workspaceSurfaceInputRevision);
        var sourceText = CommandText;
        var owner = captured.NavigationOwnerRevision;
        var name = AssistantName;
        var nameRevision = assistantNameConfiguration.Get().Revision;
        var initiallyAdmitted = captured.NavigationInitiallyAdmitted && captured.Eligible() && !IsProtectedCall;
        bool Admitted() => initiallyAdmitted && captured.Eligible() && !IsProtectedCall
            && captured.Revision == CallPolicyRevision && owner == auditImpactOwnerRevision()
            && revision == Interlocked.Read(ref workspaceSurfaceInputRevision)
            && string.Equals(sourceText, CommandText, StringComparison.Ordinal)
            && nameRevision == assistantNameConfiguration.Get().Revision
            && string.Equals(name, AssistantName, StringComparison.Ordinal);
        return Admitted;
    }
}
