using System.Text;
using Kora.Application.Diagnostics;
using Kora.Application.Memory;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private MemoryManagementService? memoryCommands;

    internal void BindMemoryCommands(MemoryManagementService service)
    {
        memoryCommands = service;
        PrivacyClosureRequested += (_, _) => service.ClearInspection();
    }

    internal async Task ExecuteMemoryCommandAsync(MemoryCommand command, SecurityAuditInitiator initiator)
    {
        if (disposed) { return; }
        if (command.Operation == MemoryCommandOperation.Invalid)
        {
            ShowInformation("Memory command not accepted.", command.Error!);
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
        bool Eligible() => !disposed && IsHostInputEligible && CallPolicyRevision == call
            && pendingModelQuestion is null && !IsGrantChangePending && !IsModelActionApprovalPending
            && (origin != RequestOrigin.ActivatedVoice || IsVoiceEnabled && HasVoiceConsent
                && CallObservation.AllowActivation && Volatile.Read(ref voiceRecoveryRevision) == recovery);
        try
        {
            var service = memoryCommands ?? throw new InvalidOperationException("The private memory management service is unavailable.");
            var result = await service.ExecuteAsync(command, origin, Eligible, CancellationToken.None);
            if (!Eligible()) { return; }
            // Do not speak, attach to history or send inspected memory to a provider.
            Transcript = "Original-user memory management; no model turn.";
            ShowInformation("Memory command " + result.Outcome + ".", Encoding.UTF8.GetString(MemoryCommandResult.Serialize(result)));
            if (command.SessionId is { } id) { sessionPresentationSources.Add(id); }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
            or UnauthorizedAccessException or OperationCanceledException or ArgumentException)
        {
            ApplicationLog.Error(logger, exception, "Running original-user memory management");
            if (disposed || !IsHostInputEligible) { return; }
            ShowFailure("Memory command not confirmed.", exception.Message
                + " Refresh the exact session/identity/revision after resolving privacy, ownership, call or storage gates."
                + " A mutation may have committed before a receipt failure; no rollback or automatic retry is claimed.");
        }
    }
}
