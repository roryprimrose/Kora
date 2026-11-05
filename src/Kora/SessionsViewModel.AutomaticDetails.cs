using Kora.Application.Presentation;
using Kora.Application.Configuration;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Presentation;

namespace Kora;

internal sealed partial class SessionsViewModel
{
    private IAutomaticDetailPresentation? automaticDetailsHost;
    private long automaticPreferenceBindingEpoch;
    private readonly AutomaticSessionDetails?[] automaticDetails = new AutomaticSessionDetails?[ConversationText.MaximumSessionMessages];
    internal long AutomaticDetailsSelectionEpoch => selectionEpoch;
    internal long AutomaticDetailsComposerEpoch => composerBufferRevision;
    internal bool CanAutomaticallyPresentDetails => CanPresentDetails && access.CanControl && !conversationHeld
        && !conversationSourceHeld && conversationPage is null && ConversationSearchMessages.Count == 0
        && !HasEvidenceDetails;

    private sealed record AutomaticDetailsCapture(AutomaticDetailPreference PreferenceEpoch, AutomaticDetailLease Lease,
        IAutomaticDetailPresentation Host, long BindingEpoch);
    private sealed record AutomaticSessionDetails(ConversationMessage Accepted, AutomaticDetailsCapture Capture);

    internal void BindAutomaticDetails(IAutomaticDetailPresentation host)
    {
        if (ReferenceEquals(automaticDetailsHost, host)) { return; }
        Array.Clear(automaticDetails);
        automaticDetailsHost = host;
    }

    private AutomaticDetailsCapture? CaptureAutomaticDetails()
    {
        if (detailPresentation?.CaptureAutomaticPreference() is not { } epoch
            || automaticDetailsHost is not { } host || host.CaptureAutomaticDetails() is not { } lease)
        { return null; }
        return new(epoch, lease, host, automaticPreferenceBindingEpoch);
    }

    private void BindAutomaticDetails(ConversationMessage accepted, AutomaticDetailsCapture? capture)
    {
        if (capture is null || capture.BindingEpoch != automaticPreferenceBindingEpoch
            || accepted.Sequence is <= 0 or > ConversationText.MaximumSessionMessages
            || capture.Lease.Session != accepted.Request.SessionId || capture.Lease.Generation != accepted.Generation)
        { return; }
        automaticDetails[checked((int)accepted.Sequence - 1)] = new(accepted, capture);
    }

    private void PresentAutomaticDetails(ConversationMessage message)
    {
        if (message.Outcome is null || message.Sequence is <= 0 or > ConversationText.MaximumSessionMessages) { return; }
        var index = checked((int)message.Sequence - 1);
        var pending = automaticDetails[index];
        automaticDetails[index] = null;
        if (pending is null || detailPresentation is not { } preference || pending.Accepted.Request != message.Request
            || pending.Accepted.Generation != message.Generation || !string.Equals(pending.Accepted.Body, message.Body, StringComparison.Ordinal)
            || message.Outcome != ModelTurnOutcome.Succeeded || message.AssistantResponse is not { } answer
            || !AnswerPresentation.From(answer.Body).HasDetails) { return; }
        var capture = pending.Capture;
        var content = new AdmittedDetailContent(new(new(message.Request.RequestId.Value), message.Revision),
            DetailContentKind.Markdown, DetailContentOrigin.FinalizedResponse, DetailSensitivity.DisclosureConfirmationRequired,
            "Current conversation answer", "Exact newly finalized assistant answer; verify important details.", answer.Body,
            new(message.Request.SessionId, message.Request.RequestId, message.Request.TaskId));
        bool Current() => !closed && ReferenceEquals(automaticDetailsHost, capture.Host)
            && capture.BindingEpoch == automaticPreferenceBindingEpoch
            && completedDetailPublication is { } publication && publication.Request == message.Request
            && publication.Generation == message.Generation && publication.Revision == message.Revision
            && !publication.Dismissed && ReferenceEquals(detailPresentation, preference)
            && CanPresentDetails && selected?.Authority.SessionId == message.Request.SessionId
            && selected.Authority.Generation == message.Generation;
        _ = capture.Host.PublishAutomaticDetails(new(message.Request, content, preference, capture.PreferenceEpoch,
            capture.Lease, Current));
    }
}
