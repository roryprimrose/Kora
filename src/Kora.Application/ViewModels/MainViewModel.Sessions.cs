using System.Text;

using Kora.Application.Hosting;
using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private SessionWorkspaceService? sessionCommands;
    private readonly HashSet<Guid> sessionPresentationSources = [];

    public void RevokeSessionPresentation(HostId<SessionIdentity> session)
    {
        if (!sessionPresentationSources.Contains(session.Value)) { return; }
        sessionPresentationSources.Clear();
        ResponseBody = "Session presentation revoked by inactivity retention. Refresh the exact retained session before inspecting it again.";
    }

    public void BindSessionCommands(SessionWorkspaceService service) => sessionCommands = service;

    internal async Task ExecuteSessionCommandAsync(SessionCommand command, SecurityAuditInitiator initiator)
    {
        if (disposed) { return; }
        if (command.Operation == SessionCommandOperation.Invalid)
        {
            ShowInformation("Session command not accepted.", command.Error!);
            return;
        }
        if (pendingModelQuestion is not null || IsGrantChangePending || IsModelActionApprovalPending)
        {
            ShowInformation("Session command blocked.", "Resolve the current question or approval explicitly first. No target or decision changed.");
            return;
        }
        var origin = initiator switch
        {
            SecurityAuditInitiator.VoiceCommand => RequestOrigin.ActivatedVoice,
            SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand => RequestOrigin.LocalUi,
            _ => RequestOrigin.HostSystem,
        };
        var callRevision = CallPolicyRevision;
        var recoveryRevision = Volatile.Read(ref voiceRecoveryRevision);
        bool Eligible() => !disposed && IsHostInputEligible && CallPolicyRevision == callRevision
            && pendingModelQuestion is null && !IsGrantChangePending && !IsModelActionApprovalPending
            && (origin != RequestOrigin.ActivatedVoice || (IsVoiceEnabled && HasVoiceConsent
                && CallObservation.AllowActivation && Volatile.Read(ref voiceRecoveryRevision) == recoveryRevision));
        if (sessionCommands is null)
        {
            ShowInformation("Session commands unavailable.", "The trusted durable workspace service is not bound. Use the native Sessions workspace when available.");
            return;
        }
        if (!Eligible())
        {
            ShowInformation("Session command denied.", "The originating channel, call observation or private host changed. Initiate a fresh command.");
            return;
        }
        if (origin == RequestOrigin.ActivatedVoice && IsProtectedCall
            && command.Operation is SessionCommandOperation.Create or SessionCommandOperation.Rename
                or SessionCommandOperation.Done or SessionCommandOperation.Resume)
        {
            ShowInformation("Voice session mutation unavailable during a protected call.",
                "The workspace mutation gate requires a clear/unavailable call observation. No delayed mutation or later confirmation is queued.");
            return;
        }
        try
        {
            var result = await sessionCommands.ExecuteCommandAsync(command, origin, Eligible, CancellationToken.None);
            if (!Eligible()) { return; }
            ShowInformation("Session command " + result.Outcome + ".", Encoding.UTF8.GetString(SessionCommandResult.Serialize(result)));
            if (command.SessionId is { } id) { sessionPresentationSources.Add(id); }
            foreach (var row in result.Sessions) { sessionPresentationSources.Add(row.Id); }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
            or UnauthorizedAccessException or OperationCanceledException)
        {
            ApplicationLog.Error(logger, exception, "Running a bounded session command");
            if (disposed || !IsHostInputEligible) { return; }
            var result = new SessionCommandResult("not-confirmed",
                exception.Message + " Resolve current questions/work and privacy/ownership/storage gates, "
                + "then inspect the exact ID and revisions before a fresh request. "
                + "A mutation may already have committed before a receipt failure; no rollback or automatic retry is claimed.");
            ShowFailure("Session command not confirmed.", Encoding.UTF8.GetString(SessionCommandResult.Serialize(result)));
        }
    }
}
