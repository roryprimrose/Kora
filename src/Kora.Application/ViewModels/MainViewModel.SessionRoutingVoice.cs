using System.ComponentModel;

using Kora.Application.Hosting.SessionRouting;
using Kora.Application.Voice;
using Kora.Core.Conversation.SessionRouting;
using Kora.Core.Voice;

namespace Kora.Application.ViewModels;

public sealed partial class MainViewModel
{
    private SessionRoutingVoiceRequest? sessionRoutingVoiceRequest;
    private SessionRoutingVoiceReply? sessionRoutingVoiceReply;
    private Task sessionRoutingVoiceRetirement = Task.CompletedTask;
    private HandsFreeVoiceContinuation.CaptureRelease? routingCaptureRelease;

    public void BindSessionRoutingCaptureRelease(HandsFreeVoiceContinuation.CaptureRelease release)
    {
        if (sessionRoutingVoiceRequest is not null)
        { throw new InvalidOperationException("An original routing request prevents replacing its capture release contract."); }
        if (voiceRecognition is HandsFreeVoiceRecognitionService handsFree) { handsFree.BindRoutingCaptureRelease(release); }
        routingCaptureRelease = release;
    }

    public SessionRoutingVoiceReply? CreateSessionRoutingVoiceReply(SessionRoutingReview review,
        IEnumerable<SessionRouteDescriptor> presented, SessionRoutingSelection? selection, Func<bool> exact)
    {
        if (sessionRoutingVoiceRequest is not { } request) { return null; }
        if (sessionRoutingVoiceReply is not null)
        { throw new InvalidOperationException("An exact routing reply still owns capture."); }
        var reply = selection is null
            ? SessionRoutingVoiceReply.ForDestinations(request, review, presented, exact, () => ObserveSessionRoutingVoiceAdmission(request), logger)
            : SessionRoutingVoiceReply.ForConfirmation(request, review, selection, exact, () => ObserveSessionRoutingVoiceAdmission(request), logger);
        sessionRoutingVoiceReply = reply;
        return reply;
    }

    public void ReleaseSessionRoutingVoiceReply(SessionRoutingVoiceReply reply)
    {
        if (!ReferenceEquals(sessionRoutingVoiceReply, reply)) { return; }
        if (!reply.Completion.IsCompletedSuccessfully)
        { throw new InvalidOperationException("The routing reply must physically join before releasing its host owner."); }
        sessionRoutingVoiceRetirement = reply.Completion;
        sessionRoutingVoiceReply = null;
    }

    private QuestionVoiceAdmission? ObserveSessionRoutingVoiceAdmission(SessionRoutingVoiceRequest request) =>
        request.IsLive && customizationClarification is null
        && activeCustomizationVoice is null && !questionSpeech && questionVoiceReplies?.IsActive != true
        && promptInputVoice?.IsActive != true && Volatile.Read(ref pushToTalkHeld) == 0
        && Volatile.Read(ref listeningStopRequests) == 0 && !listeningStop.IsStopping
            ? ObserveAdmittedVoiceCaptureReadiness() : null;

    private void RevalidateSessionRoutingVoice(object? sender, PropertyChangedEventArgs args) =>
        sessionRoutingVoiceReply?.Revalidate();

    private void RoutingVoicePrivacyClosed(object? sender, EventArgs args) => RetireSessionRoutingVoice();

    private void RetireSessionRoutingVoice()
    {
        sessionRoutingVoiceRequest?.Retire();
        sessionRoutingVoiceReply?.Cancel("Original routing admission retired. No late reply or submission is accepted.");
    }

    private async Task JoinSessionRoutingVoice()
    {
        RetireSessionRoutingVoice();
#pragma warning disable VSTHRD003 // Join the exact routing capture, including delayed opening and physical release.
        if (sessionRoutingVoiceReply is { } reply) { await reply.DisposeAsync().ConfigureAwait(false); }
        await sessionRoutingVoiceRetirement.ConfigureAwait(false);
#pragma warning restore VSTHRD003
    }
}
