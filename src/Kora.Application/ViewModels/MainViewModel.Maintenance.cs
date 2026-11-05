using Kora.Application.Diagnostics;
using Kora.Application.Maintenance;
using Kora.Core.Auditing;
using Kora.Core.Hosting;
using Kora.Core.Maintenance;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private string maintenanceStatus = "Unknown: canonical release metadata has not been checked.";
    public string MaintenanceStatus
    {
        get => maintenanceStatus;
        set => SetProperty(ref maintenanceStatus, value);
    }

    public event EventHandler? MaintenanceRequested;
    public void ShowMaintenance() => MaintenanceRequested?.Invoke(this, EventArgs.Empty);

    private IMaintenanceCommands? maintenanceCommands;
    public void BindMaintenanceCommands(IMaintenanceCommands commands) => maintenanceCommands = commands;
    private Kora.Tools.Application.ApplicationCheckForUpdates? updateCheckTool;
    private Kora.Tools.Application.ApplicationUpgrade? upgradeTool;

    public void BindMaintenanceUpdateTools(Kora.Tools.Application.ApplicationCheckForUpdates check,
        Kora.Tools.Application.ApplicationUpgrade upgrade)
    { updateCheckTool = check; upgradeTool = upgrade; }
    public bool CanRunMaintenanceCommands => !disposed && IsHostInputEligible && !IsProtectedCall
        && pendingModelQuestion is null && !IsGrantChangePending && !IsModelActionApprovalPending;

    internal async Task ExecuteMaintenanceUpdateCommandAsync(MaintenanceUpdateCommand command, SecurityAuditInitiator initiator)
    {
        if (disposed) { return; }
        if (pendingModelQuestion is not null || IsGrantChangePending || IsModelActionApprovalPending)
        {
            Transcript = "Maintenance command blocked. Resolve the current exact question or approval first; it remains unchanged.";
            return;
        }
        if (!command.IsValid)
        {
            PresentCachedMaintenance("Update command not accepted.", MaintenanceUpdateCommand.Syntax);
            return;
        }
        var origin = initiator == SecurityAuditInitiator.VoiceCommand ? RequestOrigin.ActivatedVoice
            : initiator is SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand ? RequestOrigin.LocalUi : RequestOrigin.HostSystem;
        var call = CallPolicyRevision;
        var topology = privacyObservation.Current.TopologyRevision;
        var recovery = Volatile.Read(ref voiceRecoveryRevision);
        bool Eligible() => CanRunMaintenanceCommands && CallPolicyRevision == call
            && privacyObservation.Current.TopologyRevision == topology
            && (origin != RequestOrigin.ActivatedVoice || IsVoiceEnabled && HasVoiceConsent
                && CallObservation.AllowActivation && privacyObservation.Current.CanCapture
                && Volatile.Read(ref voiceRecoveryRevision) == recovery);
        if (origin == RequestOrigin.HostSystem || !Eligible() || updateCheckTool is null || upgradeTool is null)
        {
            PresentCachedMaintenance("Update request unavailable.", "Resolve pending questions and restore current original-input, host, privacy and call admission. No check or installer was started.");
            return;
        }
        try
        {
            var output = command.Upgrade
                ? await upgradeTool.ExecuteAsync(command.Version, origin, Eligible, CancellationToken.None)
                : await updateCheckTool.ExecuteAsync(command.Version, origin, Eligible, CancellationToken.None);
            if (!Eligible()) { throw new InvalidOperationException("Update request admission changed before presentation."); }
            PresentCachedMaintenance("Release maintenance.", output);
            if (command.Upgrade) { ShowMaintenance(); }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException
            or UnauthorizedAccessException or OperationCanceledException or HttpRequestException)
        {
            ApplicationLog.Error(logger, exception, "Running a governed release maintenance action");
            if (!disposed && IsHostInputEligible)
            { PresentCachedMaintenance("Update outcome not confirmed.", "Inspect native Maintenance before retrying. No installer approval was granted by this command.", failed: true); }
        }
    }

    internal async Task ExecuteMaintenanceCommandAsync(MaintenanceCommand command, SecurityAuditInitiator initiator)
    {
        if (disposed) { return; }
        // A maintenance command is never a reply to an exact pending question or approval.
        if (pendingModelQuestion is not null || IsGrantChangePending || IsModelActionApprovalPending)
        {
            Transcript = "Maintenance command blocked. Resolve the current exact question or approval first; it remains unchanged.";
            return;
        }
        if (command == MaintenanceCommand.Invalid)
        {
            PresentCachedMaintenance("Maintenance command not accepted.", MaintenanceCommandParser.Syntax);
            return;
        }
        var origin = initiator switch
        {
            SecurityAuditInitiator.VoiceCommand => RequestOrigin.ActivatedVoice,
            SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand => RequestOrigin.LocalUi,
            _ => RequestOrigin.HostSystem,
        };
        var call = CallPolicyRevision;
        var recovery = Volatile.Read(ref voiceRecoveryRevision);
        var topology = privacyObservation.Current.TopologyRevision;
        bool Eligible() => !disposed && IsHostInputEligible && !IsProtectedCall && CallPolicyRevision == call
            && privacyObservation.Current.TopologyRevision == topology
            && pendingModelQuestion is null && !IsGrantChangePending && !IsModelActionApprovalPending
            && (origin != RequestOrigin.ActivatedVoice || IsVoiceEnabled && HasVoiceConsent
                && CallObservation.AllowActivation && privacyObservation.Current.CanCapture
                && Volatile.Read(ref voiceRecoveryRevision) == recovery);
        if (origin == RequestOrigin.HostSystem || maintenanceCommands is null || !Eligible())
        {
            PresentCachedMaintenance("Cached maintenance unavailable.", "Current original channel, owning private host, call admission and durable maintenance service are required. No check or network consent was requested.");
            return;
        }
        try
        {
            var output = await maintenanceCommands.ExecuteAsync(command, origin, Eligible, CancellationToken.None);
            if (!Eligible()) { throw new InvalidOperationException("Maintenance admission changed before presentation."); }
            PresentCachedMaintenance("Cached maintenance.", output);
            if (command == MaintenanceCommand.Review) { ShowMaintenance(); }
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or IOException
            or UnauthorizedAccessException or OperationCanceledException)
        {
            if (disposed || !IsHostInputEligible) { return; }
            try { ApplicationLog.Error(logger, exception, "Running a bounded cached maintenance command"); }
            finally
            {
                PresentCachedMaintenance("Maintenance outcome not confirmed.",
                    "Inspect native cached state after restoring ownership/privacy/call/storage admission. No automatic retry, check or browser action; a committed snooze may remain. " + exception.GetType().Name,
                    failed: true);
            }
        }
    }

    private void PresentCachedMaintenance(string title, string body, bool failed = false)
    {
        PresentResponse(failed ? Kora.Core.AssistantState.Failure : Kora.Core.AssistantState.Information,
            title, body, requestWindow: false, refreshOutput: false);
        forceVisualResponse = true;
        NotifyOutputPolicyChanged();
        if (CanRevealPrivatePresentation)
        {
            WindowActionRequested?.Invoke(this, WindowAction.Show);
        }
    }
}
