using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Application.Presentation;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public event Func<EvidenceTracePreparation, Task<bool>>? EvidenceTracePreparationRequested;
    private long evidenceTraceInputRevision;

    public void ReportEvidencePresentationUnavailable() =>
        Transcript = "Evidence reader unavailable: reader text scale is unconfirmed or preference evidence changed. Inspect saved preference evidence before explicit repair.";

    private async Task PrepareEvidenceTraceAsync(EvidenceTraceCommand command,
        SecurityAuditInitiator initiator, ManualCallInput? originalInput)
    {
        if (!command.IsValid)
        {
            Transcript = "Evidence trace command not accepted. " + EvidenceTraceCommand.Syntax;
            return;
        }
        var origin = OriginalOrigin(initiator);
        if (!isAssistantNameAvailable || initiator is not (SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || initiator == SecurityAuditInitiator.VoiceCommand && !command.IsActivated)
        {
            Transcript = "Evidence trace unavailable: use typed input or the current available assistant-name voice prefix.";
            return;
        }
        if (command.IsHelp)
        {
            Transcript = EvidenceTraceCommand.Syntax;
            return;
        }
        var input = originalInput ?? CaptureManualCallInput(origin, CallPolicyRevision);
        var revision = Interlocked.Read(ref evidenceTraceInputRevision);
        var ownerBinding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        var nameRevision = assistantNameConfiguration.Get().Revision;
        var handler = EvidenceTracePreparationRequested;
        var initiallyAdmitted = input.NavigationInitiallyAdmitted && input.Eligible() && CanRevealPrivatePresentation;
        var pending = true;
        string? retiredBoundary = null;
        bool Retired(string boundary) { retiredBoundary = boundary; return false; }
        bool Current()
        {
            if (!pending || !initiallyAdmitted) { return Retired("original admission"); }
            if (!input.Eligible() || !CanRevealPrivatePresentation) { return Retired("private host or original channel"); }
            if (input.Revision != CallPolicyRevision) { return Retired("call policy"); }
            if (input.NavigationOwnerRevision != auditImpactOwnerRevision()
                || ownerBinding != Interlocked.Read(ref settingsOwnershipBindingRevision)) { return Retired("ownership"); }
            if (nameRevision != assistantNameConfiguration.Get().Revision) { return Retired("assistant name"); }
            if (revision != Interlocked.Read(ref evidenceTraceInputRevision)) { return Retired("original input"); }
            if (!ReferenceEquals(handler, EvidenceTracePreparationRequested)) { return Retired("native binding"); }
            return true;
        }
        if (!Current() || handler is null)
        {
            Transcript = "Evidence trace unavailable: original private host admission or native Evidence binding is unavailable.";
            return;
        }
        try
        {
            await HostRequestRunner.RunAsync(origin, async () =>
            {
                var prepared = await handler(new(command.TraceId!, Current));
                if (!prepared || !Current())
                {
                    Transcript = "Evidence trace unavailable: the original native view did not confirm exact filter preparation and focus. Nothing was queried.";
                    if (retiredBoundary is not null) { Transcript += " Retired boundary: " + retiredBoundary + "."; }
                    return;
                }
                Transcript = "Evidence trace filter prepared in the focused native Evidence viewer. "
                    + "All other filters and the selected source remain unchanged and AND-composed. "
                    + "Use Search / refresh separately; nothing was read or scanned. Correlation is not authority.";
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException
            or IOException or UnauthorizedAccessException)
        {
            ApplicationLog.Error(logger, exception, "Preparing the native Evidence trace filter");
            Transcript = "Evidence trace unavailable: native preparation failed or was cancelled. Nothing was queried.";
        }
        finally { pending = false; }
    }
}
