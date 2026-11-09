using Kora.Application.Voice;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.Hosting.SessionRouting;

/// <summary>Volatile original routing authority, with an exact independently joined reply handover.</summary>
public sealed class SessionRoutingVoiceRequest
{
    private readonly Func<bool> admitted;
    private readonly Lock gate = new();
    private readonly HandsFreeVoiceContinuation? wake;
    private readonly HandsFreeVoiceContinuation.CaptureRelease? release;
    private readonly RoutingCaptureRelease? captureRelease;
    private readonly IQuestionVoiceRecognitionService? hostCapture;
    private long generation;
    private int retired;
    private int handover;
    private HandsFreeVoiceRecognitionService? directCapture;

    public SessionRoutingVoiceRequest(string body, HostRequest request, Func<bool> admitted,
        IQuestionVoiceRecognitionService? capture, HandsFreeVoiceContinuation? wake = null,
        HandsFreeVoiceContinuation.CaptureRelease? release = null)
    {
        SessionConversationService.RequireOriginalSource(request.Origin);
        if (wake is not null && !ReferenceEquals(wake.Request, request))
        { throw new InvalidOperationException("Routing continuation belongs only to its original host wake request."); }
        Body = body;
        Request = request;
        this.admitted = admitted;
        this.wake = wake;
        this.release = wake is null ? release : wake.Release;
        hostCapture = capture;
        Capture = this.release is null ? null : wake?.Capture
            ?? (capture is HandsFreeVoiceRecognitionService handsFree ? handsFree.RoutingActivatedCapture : capture);
        captureRelease = Capture is null ? null : wake?.CaptureReleaseOwner
            ?? new(Capture, this.release!);
        generation = Capture?.CaptureGeneration ?? 0;
    }

    public string Body { get; }
    public HostRequest Request { get; }
    internal IQuestionVoiceRecognitionService? Capture { get; }
    public bool IsLive => Volatile.Read(ref retired) == 0 && admitted()
        && (wake?.IsAdmitted ?? true)
        && (Volatile.Read(ref handover) != 0 || Capture is null || generation == Capture.CaptureGeneration);
    public bool IsCurrent => IsLive && Volatile.Read(ref handover) == 0 && (wake?.IsCurrent ?? true);

    internal bool BeginHandover()
    {
        lock (gate)
        {
            if (Capture is null || !IsCurrent) { return false; }
            DetachDirectCapture();
            captureRelease!.BeginCapture();
            Volatile.Write(ref handover, 1);
            return true;
        }
    }

    internal Task OpenAsync(MicrophoneDevice microphone, IEnumerable<string> phrases, Func<bool> exact, CancellationToken token) =>
        wake is null ? hostCapture!.BeginReplyAsync(microphone, phrases, exact, token) : wake.BeginReplyAsync(microphone, phrases, exact, token);

    internal void Acknowledge(long successor)
    {
        wake?.Acknowledge(successor);
        captureRelease!.CaptureStarted();
        if (wake is null && hostCapture is HandsFreeVoiceRecognitionService handsFree)
        {
            handsFree.AttachRoutingCapture(captureRelease);
            directCapture = handsFree;
        }
    }

    internal async Task ReleaseAsync(long successor)
    {
        var captured = Capture!.CaptureGeneration;
        if (captured != (successor >= 0 ? successor : generation)) { Retire(); }
        if (wake is not null)
        {
            await wake.ReleaseAsync().ConfigureAwait(false);
            generation = wake.ExpectedGeneration;
        }
        else { generation = await captureRelease!.JoinAsync().ConfigureAwait(false); }
        Volatile.Write(ref handover, 0);
        if (!IsCurrent) { Retire(); }
    }

    public void Retire() => Interlocked.Exchange(ref retired, 1);

    internal void InvalidateHostCapture(IVoiceRecognitionService host)
    {
        RequireHostCapture(host);
        Retire();
        if (captureRelease is null) { host.InvalidateCapture(); }
        else { captureRelease.Invalidate(host.InvalidateCapture); }
    }

    internal Task StopHostCaptureAsync(IVoiceRecognitionService host)
    {
        RequireHostCapture(host);
        Retire();
        if (captureRelease is null) { return host.StopAsync(); }
        if (captureRelease.IsCaptureActive)
        {
            return ReferenceEquals(Capture, host) ? captureRelease.JoinRetiredAsync() : host.StopAsync();
        }
        return captureRelease.StopHostAsync(() => host.StopAsync());
    }

    private void RequireHostCapture(IVoiceRecognitionService host)
    {
        if (captureRelease is not null && !ReferenceEquals(host, hostCapture) && !ReferenceEquals(host, Capture)
            && !ReferenceEquals(host, wake?.Owner))
        { throw new InvalidOperationException("A foreign host cannot mutate this routing capture."); }
    }

    internal void ReleaseOwner()
    {
        Retire();
        wake?.Retire();
        DetachDirectCapture();
    }

    private void DetachDirectCapture()
    {
        if (directCapture is null) { return; }
        directCapture.DetachRoutingCapture(captureRelease!);
        directCapture = null;
    }
}
