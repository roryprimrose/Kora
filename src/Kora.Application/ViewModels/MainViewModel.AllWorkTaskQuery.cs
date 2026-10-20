using Kora.Application.Hosting;
using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Commands;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public event Func<AllWorkTaskQueryPreparation, Task<bool>>? AllWorkTaskQueryPreparationRequested;

    private async Task ExecuteAllWorkTaskQueryCommand(
        AllWorkTaskQueryCommand command, string input, SecurityAuditInitiator initiator, ManualCallInput? originalInput)
    {
        if (!command.IsValid)
        {
            Transcript = "All Work query command not accepted. " + AllWorkTaskQueryCommand.Guidance;
            return;
        }
        var admitted = CaptureWorkspaceSurfaceAdmission(input, initiator, originalInput,
            preserveResponse: true, activated: command.IsActivated);
        if (admitted is null) { return; }
        if (!admitted())
        {
            Transcript = "All Work query unavailable: original private host admission changed. Nothing was read or prepared.";
            return;
        }
        if (command.IsHelp)
        {
            Transcript = AllWorkTaskQueryCommand.Guidance;
            return;
        }
        var handler = AllWorkTaskQueryPreparationRequested;
        if (handler is null)
        {
            Transcript = "All Work query unavailable: the native preparation route is not bound. Nothing was read or prepared.";
            return;
        }
        bool Current() => admitted() && ReferenceEquals(handler, AllWorkTaskQueryPreparationRequested);
        var prepared = false;
        var preparation = new AllWorkTaskQueryPreparation(command.TaskId!.Value, Current);
        try
        {
            await HostRequestRunner.RunAsync(OriginalOrigin(initiator), async () =>
                prepared = await handler(preparation));
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException
            or UnauthorizedAccessException or OperationCanceledException or ArgumentException)
        {
            ApplicationLog.Error(logger, exception, "Preparing native All Work task correlation");
        }
        if (!prepared || !preparation.IsPrepared)
        {
            Transcript = "All Work query unavailable: native focus/preparation did not acknowledge the original admitted source. No scan ran.";
            return;
        }
        Transcript = "All Work query PREPARED / REVIEW NEEDED. " + AllWorkTaskQueryCommand.Guidance;
    }
}
