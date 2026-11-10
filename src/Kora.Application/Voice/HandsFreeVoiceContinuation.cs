using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.Voice;

/// <summary>Exact first-party successor capture for one already consumed wake request.</summary>
public sealed class HandsFreeVoiceContinuation
{
    internal HandsFreeVoiceContinuation(HandsFreeVoiceRecognitionService owner,
        VoiceTranscriptEventArgs utterance, HostRequest request, IQuestionVoiceRecognitionService capture)
    {
        Owner = owner;
        Utterance = utterance;
        Request = request;
        Capture = capture;
        Release = owner.RoutingCaptureRelease;
        CaptureReleaseOwner = Release is null ? null : new(capture, Release);
        ExpectedGeneration = capture.CaptureGeneration;
    }

    internal HandsFreeVoiceRecognitionService Owner { get; }
    internal VoiceTranscriptEventArgs Utterance { get; }
    internal HostRequest Request { get; }
    internal IQuestionVoiceRecognitionService Capture { get; }
    internal CaptureRelease? Release { get; }
    internal RoutingCaptureRelease? CaptureReleaseOwner { get; }
    internal long ExpectedGeneration { get; set; }
    internal ContinuationPhase Phase { get; set; }
    internal bool Retired { get; set; }
    internal enum ContinuationPhase { Ready, Opening, Capturing, Releasing }

    public bool IsCurrent => Owner.IsRoutingContinuationCurrent(this, requireReady: true);
    internal bool IsAdmitted => Owner.IsRoutingContinuationCurrent(this, requireReady: false);
    internal Task BeginReplyAsync(MicrophoneDevice microphone, IEnumerable<string> phrases,
        Func<bool> admitted, CancellationToken token) => Owner.BeginRoutingReplyAsync(this, microphone, phrases, admitted, token);
    internal void Acknowledge(long generation) => Owner.AcknowledgeRoutingReply(this, generation);
    internal Task ReleaseAsync() => Owner.ReleaseRoutingReplyAsync(this);
    internal void Retire() => Owner.RetireRoutingContinuation(this);

    public sealed class CaptureRelease
    {
        private readonly int advance;
        private CaptureRelease(int advance) => this.advance = advance;
        public static CaptureRelease Preserved { get; } = new(0);
        public static CaptureRelease InvalidatedOnce { get; } = new(1);
        internal long Expected(long generation) => checked(generation + advance);
    }

}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "MA0048",
    Justification = "The assigned routing-continuation file co-locates its typed authority with the owning hands-free partial.")]
public sealed partial class HandsFreeVoiceRecognitionService
{
    private HandsFreeVoiceContinuation? routingContinuation;
    private RoutingCaptureRelease? directRoutingCapture;
    internal IQuestionVoiceRecognitionService RoutingActivatedCapture => activated;
    internal HandsFreeVoiceContinuation.CaptureRelease? RoutingCaptureRelease { get; private set; }

    internal void BindRoutingCaptureRelease(HandsFreeVoiceContinuation.CaptureRelease release)
    {
        lock (gate)
        {
            if (routingContinuation is not null || directRoutingCapture is not null)
            { throw new InvalidOperationException("An original routing owner prevents replacing its release contract."); }
            RoutingCaptureRelease = release;
        }
    }

    internal void AttachRoutingCapture(RoutingCaptureRelease release)
    {
        lock (gate)
        {
            if (directRoutingCapture is not null || routingContinuation is not null)
            { throw new InvalidOperationException("Another routing owner already holds capture."); }
            directRoutingCapture = release;
        }
    }

    internal void DetachRoutingCapture(RoutingCaptureRelease release)
    {
        lock (gate)
        {
            if (!ReferenceEquals(directRoutingCapture, release))
            { throw new InvalidOperationException("The exact direct routing capture owner changed."); }
            directRoutingCapture = null;
        }
    }

    private RoutingCaptureRelease? RoutingCaptureOwner
    {
        get { lock (gate) { return routingContinuation?.CaptureReleaseOwner ?? directRoutingCapture; } }
    }

    private void MutateRoutingCapture(Action mutate)
    {
        var captureOwner = RoutingCaptureOwner;
        if (captureOwner is null) { mutate(); }
        else { captureOwner.Invalidate(mutate); }
    }

    internal HandsFreeVoiceContinuation CaptureRoutingContinuation(VoiceTranscriptEventArgs utterance, HostRequest request)
    {
        var observation = ObserveAdmission(requireCaptureOwner: true);
        var quiescent = activated.IsCaptureQuiescent;
        lock (gate)
        {
            if (routingContinuation is not null || directRoutingCapture is not null || !wakeUtteranceConsumed
                || !IsWakeActivationCurrentCore(utterance, request, observation, quiescent))
            { throw new InvalidOperationException("Only the exact consumed original wake owner can acquire routing continuation."); }
            return routingContinuation = new(this, utterance, request, activated);
        }
    }

    internal bool IsRoutingContinuationCurrent(HandsFreeVoiceContinuation continuation, bool requireReady)
    {
        var observation = ObserveAdmission();
        var generation = activated.CaptureGeneration;
        var quiescent = activated.IsCaptureQuiescent;
        lock (gate)
        {
            var current = RoutingAuthorityCurrent(continuation, observation)
                && (continuation.Phase == HandsFreeVoiceContinuation.ContinuationPhase.Opening
                    || continuation.Phase == HandsFreeVoiceContinuation.ContinuationPhase.Releasing
                    || continuation.ExpectedGeneration == generation)
                && (continuation.Phase != HandsFreeVoiceContinuation.ContinuationPhase.Ready || quiescent);
            if (!current) { continuation.Retired = true; }
            return current && (!requireReady || continuation.Phase == HandsFreeVoiceContinuation.ContinuationPhase.Ready);
        }
    }

    private bool RoutingAuthorityCurrent(HandsFreeVoiceContinuation continuation, AdmissionObservation? observation) =>
        ReferenceEquals(routingContinuation, continuation) && !continuation.Retired
        && ReferenceEquals(continuation.Owner, this) && state == HandsFreeVoiceState.CompletingOperation
        && ReferenceEquals(pendingWakeUtterance, continuation.Utterance)
        && continuation.Utterance.ActivationEvidence is NativeWakeEvidence evidence
        && ReferenceEquals(evidence.Request, continuation.Request)
        && evidence.Generation == ambientGeneration && ReferenceEquals(admission, evidence.Admission)
        && IsObservedAdmissionCurrent(observation) && captureClosed.Task.IsCompletedSuccessfully && session is null;

    internal async Task BeginRoutingReplyAsync(HandsFreeVoiceContinuation continuation, MicrophoneDevice microphone,
        IEnumerable<string> phrases, Func<bool> admitted, CancellationToken token)
    {
        if (continuation.Release is null)
        { throw new InvalidOperationException("Routing reply capture has no host-bound release contract."); }
        if (!continuation.IsCurrent || !admitted()) { throw new OperationCanceledException("Original routing capture retired."); }
        var observation = ObserveAdmission();
        lock (gate)
        {
            if (!RoutingAuthorityCurrent(continuation, observation)
                || continuation.Phase != HandsFreeVoiceContinuation.ContinuationPhase.Ready)
            { throw new InvalidOperationException("Another capture owner or profile transition prevents routing handover."); }
            continuation.Phase = HandsFreeVoiceContinuation.ContinuationPhase.Opening;
            activatedOpenRequests++;
        }
        try
        {
            await activated.BeginReplyAsync(microphone, phrases, () => continuation.IsAdmitted && admitted(), token);
            continuation.CaptureReleaseOwner!.CaptureStarted();
            if (!continuation.IsAdmitted || !admitted()) { throw new OperationCanceledException("Routing successor admission changed."); }
        }
        finally { lock (gate) { activatedOpenRequests--; } }
    }

    internal void AcknowledgeRoutingReply(HandsFreeVoiceContinuation continuation, long generation)
    {
        if (!continuation.IsAdmitted) { throw new OperationCanceledException("Original wake authority changed."); }
        lock (gate)
        {
            if (continuation.Retired || !ReferenceEquals(routingContinuation, continuation)
                || continuation.Phase != HandsFreeVoiceContinuation.ContinuationPhase.Opening
                || activated.CaptureGeneration != generation)
            { throw new InvalidOperationException("The exact routing successor generation was not opened."); }
            continuation.ExpectedGeneration = generation;
            continuation.Phase = HandsFreeVoiceContinuation.ContinuationPhase.Capturing;
        }
    }

    internal async Task ReleaseRoutingReplyAsync(HandsFreeVoiceContinuation continuation)
    {
        var generation = activated.CaptureGeneration;
        bool retryClose;
        lock (gate)
        {
            if (!ReferenceEquals(routingContinuation, continuation))
            { throw new InvalidOperationException("A foreign or retired routing owner cannot release capture."); }
            if (continuation.ExpectedGeneration != generation)
            { continuation.Retired = true; }
            retryClose = continuation.Retired && continuation.Phase == HandsFreeVoiceContinuation.ContinuationPhase.Releasing
                && continuation.CaptureReleaseOwner!.HasFailedRelease;
            continuation.Phase = HandsFreeVoiceContinuation.ContinuationPhase.Releasing;
        }
        try
        {
            var released = await (retryClose ? continuation.CaptureReleaseOwner!.JoinRetiredAsync()
                : continuation.CaptureReleaseOwner!.JoinAsync()).ConfigureAwait(false);
            var observation = ObserveAdmission();
            lock (gate)
            {
                if (!RoutingAuthorityCurrent(continuation, observation)) { continuation.Retired = true; }
                continuation.ExpectedGeneration = released;
                continuation.Phase = HandsFreeVoiceContinuation.ContinuationPhase.Ready;
            }
        }
        catch
        {
            lock (gate) { continuation.Retired = true; }
            throw;
        }
    }

    internal void RetireRoutingContinuation(HandsFreeVoiceContinuation continuation)
    {
        lock (gate)
        {
            continuation.Retired = true;
            if (ReferenceEquals(routingContinuation, continuation))
            {
                if (continuation.Phase != HandsFreeVoiceContinuation.ContinuationPhase.Ready)
                { throw new InvalidOperationException("Routing successor ownership must be joined before retirement."); }
                routingContinuation = null;
            }
        }
    }
}
