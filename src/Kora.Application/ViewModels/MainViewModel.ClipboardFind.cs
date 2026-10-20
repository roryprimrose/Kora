using Kora.Application.Presentation;
using Kora.Core.Auditing;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    public event EventHandler<PreparedClipboardFindEventArgs>? ClipboardFindPreparationRequested;

    private sealed record ClipboardFindInput(
        ClipboardFindCommand Command, ClipboardSnapshot? Snapshot, RequestOrigin Origin, Func<bool> Eligible);

    private ClipboardFindInput? CaptureClipboardFindInput(string text, SecurityAuditInitiator initiator, bool queued = false)
    {
        if (ClipboardFindCommand.Parse(text, AssistantName) is not { } command) { return null; }
        var origin = OriginalOrigin(initiator);
        var call = CallPolicyRevision;
        var voice = Volatile.Read(ref voiceRecoveryRevision);
        var voiceGeneration = voiceRecognition.Generation;
        var navigationInput = Interlocked.Read(ref workspaceSurfaceInputRevision) + (queued ? 1 : 0);
        var privacy = privacyObservation.Current.TopologyRevision;
        var owner = auditImpactOwnerRevision();
        var ownerBinding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        var ownerGate = clipboardOwnershipEligible;
        var voiceOwner = voiceOwnershipEligible;
        var name = AssistantName;
        bool Eligible() => isAssistantNameAvailable && string.Equals(AssistantName, name, StringComparison.Ordinal)
            && ReferenceEquals(clipboardOwnershipEligible, ownerGate) && owner == auditImpactOwnerRevision()
            && ownerBinding == Interlocked.Read(ref settingsOwnershipBindingRevision)
            && IsClipboardEligible(origin, call) && privacyObservation.Current.TopologyRevision == privacy
            && (origin != RequestOrigin.ActivatedVoice || (command.IsActivated && IsVoiceEnabled && HasVoiceConsent
                && ReferenceEquals(voiceOwnershipEligible, voiceOwner) && voiceOwnershipEligible()
                && voiceRecognition.Generation == voiceGeneration && Volatile.Read(ref voiceRecoveryRevision) == voice));
        var initiallyEligible = Eligible();
        var snapshot = command.IsValid && initiallyEligible ? ClipboardPreview : null;
        return new(command, snapshot, origin, () => initiallyEligible && Eligible()
            && navigationInput == Interlocked.Read(ref workspaceSurfaceInputRevision));
    }

    private void PrepareClipboardFind(ClipboardFindInput input)
    {
        var command = input.Command;
        var revision = Interlocked.Read(ref approvalPreparationRevision);
        bool Eligible() => input.Eligible() && revision == Interlocked.Read(ref approvalPreparationRevision);
        if (!command.IsValid || !Eligible())
        {
            PresentClipboardFindPreparation(false);
            return;
        }
        var snapshot = input.Snapshot;
        if (snapshot is null || snapshot.SnapshotId != command.SnapshotId || !ReferenceEquals(ClipboardPreview, snapshot))
        {
            PresentClipboardFindPreparation(false);
            return;
        }
        bool Current() => Eligible() && ReferenceEquals(ClipboardPreview, snapshot)
            && snapshot.SnapshotId == command.SnapshotId;
        var begin = HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Presentation, input.Origin);
        uiDispatcher.Post(() =>
        {
            using var activity = begin();
            using var request = new PreparedClipboardFindEventArgs(snapshot, command.Query!, Current);
            if (request.IsCurrent) { ClipboardFindPreparationRequested?.Invoke(this, request); }
            var prepared = request.IsPrepared && Current();
            PresentClipboardFindPreparation(prepared);
            activity.Complete(prepared ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
        });
    }

    private void PresentClipboardFindPreparation(bool prepared)
    {
        if (disposed || !IsHostInputEligible) { return; }
        ShowInformation(prepared ? "Clipboard Find PREPARED / REVIEW NEEDED" : "Clipboard Find unavailable / REVIEW NEEDED",
            (prepared ? "The same retained snapshot's native query field is prepared. No Find has run. "
                : "Use the exact retained snapshot ID, valid literal and original private local input. Nothing was searched or recaptured. ")
            + ClipboardFindCommand.Syntax);
    }
}
