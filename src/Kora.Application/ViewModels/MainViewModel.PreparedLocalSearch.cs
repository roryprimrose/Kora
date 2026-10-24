using Kora.Application.Presentation;
using Kora.Core.Auditing;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public event EventHandler<PreparedLocalSearchEventArgs>? LocalSearchPreparationRequested;

    private void PrepareLocalSearch(PreparedLocalSearchCommand command, SecurityAuditInitiator initiator)
    {
        var origin = OriginalOrigin(initiator);
        var call = CallPolicyRevision;
        var voice = Volatile.Read(ref voiceRecoveryRevision);
        var input = Interlocked.Read(ref approvalPreparationRevision);
        var privacy = privacyObservation.Current.TopologyRevision;
        var name = AssistantName;
        bool Eligible() => isAssistantNameAvailable && string.Equals(AssistantName, name, StringComparison.Ordinal)
            && IsClipboardEligible(origin, call)
            && privacyObservation.Current.TopologyRevision == privacy
            && input == Interlocked.Read(ref approvalPreparationRevision)
            && (origin != RequestOrigin.ActivatedVoice || (command.IsActivated && IsVoiceEnabled && HasVoiceConsent
                && voiceOwnershipEligible() && Volatile.Read(ref voiceRecoveryRevision) == voice));
        if (!command.IsValid || !Eligible())
        {
            PresentSearchPreparation(false);
            return;
        }
        var file = FileRevision;
        var folder = FolderRevision;
        if (command.IsFolder ? folder is null || file is not null : file is null || folder is not null)
        {
            PresentSearchPreparation(false);
            return;
        }
        bool Current() => Eligible() && ReferenceEquals(FileRevision, file) && ReferenceEquals(FolderRevision, folder);
        var begin = HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Presentation, origin);
        uiDispatcher.Post(() =>
        {
            using var activity = begin();
            using var request = new PreparedLocalSearchEventArgs(file?.Reference, folder?.Reference, command.Query!, Current);
            if (request.IsCurrent) { LocalSearchPreparationRequested?.Invoke(this, request); }
            var prepared = request.IsPrepared && Current();
            PresentSearchPreparation(prepared);
            activity.Complete(prepared ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
        });
    }

    private void PresentSearchPreparation(bool prepared)
    {
        if (disposed || !IsHostInputEligible) { return; }
        ShowInformation(prepared ? "Local search PREPARED / REVIEW NEEDED" : "Local search preparation refused / REVIEW NEEDED",
            (prepared ? "The exact preview's native query field is prepared. No search has run. "
                : "Use a current explicit file or complete folder preview and valid original local input. Nothing was searched. ")
            + PreparedLocalSearchCommand.Syntax);
    }
}
