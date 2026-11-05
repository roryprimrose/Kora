using Kora.Application.Configuration;
using Kora.Application.Presentation;
using Kora.Application.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    internal Func<bool> CaptureAutomaticDetailsAdmission()
    {
        var binding = Interlocked.Read(ref settingsOwnershipBindingRevision);
        var owner = settingsOwnershipRevision();
        var privacy = privacyObservation.Current.TopologyRevision;
        var recovery = Interlocked.Read(ref voiceRecoveryRevision);
        var call = CallPolicyRevision;
        var input = Interlocked.Read(ref workspaceSurfaceInputRevision);
        var commandInput = Interlocked.Read(ref attachmentCatalogueComposerRevision);
        var capture = voiceRecognition.CaptureGeneration;
        bool Current() => IsCallMutationHostEligible && !IsProtectedCall && !IsResponseInteractionPending
            && !IsGrantEditorVisible && !HasResponseActions && voiceRecognition.IsCaptureQuiescent
            && composerDictation?.IsActive != true && !IsSpeaking
            && binding == Interlocked.Read(ref settingsOwnershipBindingRevision) && owner == settingsOwnershipRevision()
            && privacy == privacyObservation.Current.TopologyRevision && recovery == Interlocked.Read(ref voiceRecoveryRevision)
            && call == CallPolicyRevision && input == Interlocked.Read(ref workspaceSurfaceInputRevision)
            && commandInput == Interlocked.Read(ref attachmentCatalogueComposerRevision)
            && capture == voiceRecognition.CaptureGeneration;
        var initially = Current();
        return () => initially && Current();
    }

    private sealed record AutomaticResponseCapture(DetailPresentationConfigurationService Preference,
        AutomaticDetailPreference Epoch, IAutomaticDetailPresentation Host, AutomaticDetailLease Lease,
        Func<bool> Current, Func<bool> SourceCurrent);

    private AutomaticResponseCapture? CaptureAutomaticResponse()
    {
        if (detailPresentation is not { } preference || preference.CaptureAutomaticPreference() is not { } epoch
            || currentResponsePresentation is not IAutomaticDetailPresentation host
            || host.CaptureAutomaticDetails() is not { } lease) { return null; }
        var binding = Interlocked.Read(ref currentResponseBindingRevision);
        var admission = CaptureAutomaticDetailsAdmission();
        bool SourceCurrent() => IsCallMutationHostEligible && ReferenceEquals(detailPresentation, preference)
            && ReferenceEquals(currentResponsePresentation, host)
            && binding == Interlocked.Read(ref currentResponseBindingRevision);
        return new(preference, epoch, host, lease, () => admission() && SourceCurrent(), SourceCurrent);
    }

    private void PresentAutomaticResponse(AutomaticResponseCapture? capture)
    {
        if (capture is null || Volatile.Read(ref ordinaryResponse) is not
            { ModelAnswer: { } answer } source || !capture.Current() || !AnswerPresentation.From(answer.Body).HasDetails
            || answer.Request.SessionId != capture.Lease.Session)
        { return; }
        var request = answer.Request;
        AdmittedDetailContent content;
        try
        {
            content = new(new(new(source.Id), source.Revision), DetailContentKind.Markdown,
                DetailContentOrigin.FinalizedResponse, DetailSensitivity.DisclosureConfirmationRequired, "Current response",
                "Exact newly finalized model answer; verify important details.", answer.Body,
                new(request.SessionId, request.RequestId, request.TaskId));
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            CurrentResponseActionLog.CurrentResponseActionFailed(logger, exception.GetType().Name);
            return;
        }
        bool Current() => capture.SourceCurrent() && ReferenceEquals(source, Volatile.Read(ref ordinaryResponse));
        _ = capture.Host.PublishAutomaticDetails(new(request, content, capture.Preference, capture.Epoch, capture.Lease, Current));
    }
}
