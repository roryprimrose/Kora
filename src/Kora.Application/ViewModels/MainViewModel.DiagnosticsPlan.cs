using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private ISessionDiagnosticsPlanPresenter? diagnosticsPlanPresenter;
    private long diagnosticsPlanEpoch;

    public void BindSessionDiagnosticsPlan(ISessionDiagnosticsPlanPresenter? presenter)
    {
        Interlocked.Increment(ref diagnosticsPlanEpoch);
        diagnosticsPlanPresenter = presenter;
    }

    internal async Task ExecuteDiagnosticsPlanAsync(SessionDiagnosticsPlanCommand command,
        SecurityAuditInitiator initiator, bool activationPrefixed)
    {
        if (disposed) { return; }
        var epoch = Interlocked.Increment(ref diagnosticsPlanEpoch);
        if (command.Error is not null || command.IsHelp)
        {
            ShowInformation(command.IsHelp ? "Session diagnostics plan help." : "Session diagnostics plan not accepted.",
                command.Error ?? SessionDiagnosticsPlanCommand.Syntax + ". " + SessionDiagnosticsPlanCommand.Boundary);
            return;
        }
        if (pendingModelQuestion is not null || IsGrantChangePending || IsModelActionApprovalPending)
        {
            ShowInformation("Session diagnostics plan blocked.", "Resolve the current question or approval explicitly first. No target or decision changed.");
            return;
        }
        var origin = initiator switch
        {
            SecurityAuditInitiator.VoiceCommand => RequestOrigin.ActivatedVoice,
            SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand => RequestOrigin.LocalUi,
            _ => RequestOrigin.HostSystem,
        };
        var presenter = diagnosticsPlanPresenter;
        var callRevision = CallPolicyRevision;
        var recoveryRevision = Volatile.Read(ref voiceRecoveryRevision);
        var originalName = AssistantName;
        bool Eligible() => !disposed && epoch == Interlocked.Read(ref diagnosticsPlanEpoch)
            && ReferenceEquals(presenter, diagnosticsPlanPresenter) && IsHostInputEligible && !IsProtectedCall
            && CallPolicyRevision == callRevision && pendingModelQuestion is null
            && !IsGrantChangePending && !IsModelActionApprovalPending
            && (origin == RequestOrigin.LocalUi || origin == RequestOrigin.ActivatedVoice
                && activationPrefixed && IsVoiceEnabled && HasVoiceConsent && CallObservation.AllowActivation
                && recoveryRevision == Volatile.Read(ref voiceRecoveryRevision)
                && string.Equals(originalName, AssistantName, StringComparison.Ordinal));
        if (!Eligible())
        {
            ShowInformation("Session diagnostics plan denied.", "Use original typed input or the current activated assistant name with unchanged private host admission. Nothing was queued.");
            return;
        }
        if (presenter is null)
        {
            ShowInformation("Session diagnostics plan unavailable.", "The private native Sessions review is not bound. No alternate session or model fallback is used.");
            return;
        }
        try
        {
            // The native presenter captures the original review before queueing its window opening.
            var displayed = await presenter.PrepareDiagnosticsPlanAsync(command, origin, Eligible, CancellationToken.None);
            if (!Eligible()) { return; }
            ShowInformation(displayed ? "Session diagnostics plan prepared." : "Session diagnostics plan not opened.",
                SessionDiagnosticsPlanCommand.Boundary + " If cancelled or stale, request a fresh exact plan.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
            or UnauthorizedAccessException or OperationCanceledException or ArgumentException)
        {
            ApplicationLog.SessionDiagnosticsPlanFailed(logger, exception.GetType().Name);
            if (!Eligible()) { return; }
            ShowFailure("Session diagnostics plan unavailable.", "Nothing was queued or dispatched. Restore private ownership and request a fresh exact plan.");
        }
    }
}
