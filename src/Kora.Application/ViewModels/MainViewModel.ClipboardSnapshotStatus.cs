using System.Globalization;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Tools.Clipboard;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public Task ShowClipboardSnapshotStatusAsync(Guid snapshotId, CancellationToken cancellationToken = default)
    {
        var origin = HostActivity.Current?.Request.Origin ?? RequestOrigin.LocalUi;
        return ShowClipboardSnapshotStatusAsync(snapshotId,
            CaptureManualCallInput(origin, CallPolicyRevision), cancellationToken);
    }

    private Task ShowClipboardSnapshotStatusAsync(Guid snapshotId, ManualCallInput input, CancellationToken cancellationToken)
    {
        var name = AssistantName;
        var inputRevision = Interlocked.Read(ref approvalPreparationRevision);
        bool Eligible() => input.Eligible() && isAssistantNameAvailable && string.Equals(AssistantName, name, StringComparison.Ordinal)
            && inputRevision == Interlocked.Read(ref approvalPreparationRevision)
            && IsClipboardEligible(input.Origin, input.Revision);
        return HostRequestRunner.RunAsync(input.Origin, () =>
        {
            new ClipboardSnapshotStatus(clipboardPreview).Execute(snapshotId,
                Eligible,
                result =>
                {
                    if (!disposed && IsHostInputEligible)
                    {
                        Transcript = result.IsPresent
                            ? "Clipboard snapshot present: " + result.SnapshotId!.Value.ToString("D") + "; "
                                + result.Utf8Bytes!.Value.ToString(CultureInfo.InvariantCulture) + " UTF-8 bytes."
                            : "Clipboard snapshot unavailable. No clipboard read or source use was requested.";
                    }
                }, cancellationToken);
            return Task.CompletedTask;
        });
    }

    private Task ExecuteClipboardSnapshotStatusCommandAsync(
        ClipboardSnapshotStatusCommand command, SecurityAuditInitiator initiator, ManualCallInput input)
    {
        if (!IsHostInputEligible) { return Task.CompletedTask; }
        if (!command.IsValid || (initiator == SecurityAuditInitiator.VoiceCommand && !command.IsActivated))
        {
            Transcript = ClipboardSnapshotStatusCommand.Syntax
                + " Spoken commands require the current assistant-name prefix.";
            return Task.CompletedTask;
        }
        if (command.IsHelp)
        {
            Transcript = ClipboardSnapshotStatusCommand.Syntax;
            return Task.CompletedTask;
        }
        return ShowClipboardSnapshotStatusAsync(command.SnapshotId, input, CancellationToken.None);
    }
}
