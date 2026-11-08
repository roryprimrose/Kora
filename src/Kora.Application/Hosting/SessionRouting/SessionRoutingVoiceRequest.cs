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
    private long generation;
    private int retired;
    private int handover;

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
        Capture = this.release is null ? null : wake?.Capture ?? capture;
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
            Volatile.Write(ref handover, 1);
            return true;
        }
    }

    internal Task OpenAsync(MicrophoneDevice microphone, IEnumerable<string> phrases, Func<bool> exact, CancellationToken token) =>
        wake is null ? Capture!.BeginReplyAsync(microphone, phrases, exact, token) : wake.BeginReplyAsync(microphone, phrases, exact, token);

    internal void Acknowledge(long successor) => wake?.Acknowledge(successor);

    internal async Task ReleaseAsync(long successor)
    {
        var captured = Capture!.CaptureGeneration;
        if (captured != (successor >= 0 ? successor : generation)) { Retire(); }
        var released = release!.Expected(captured, wake is null && Capture is HandsFreeVoiceRecognitionService);
        if (wake is not null) { await wake.ReleaseAsync().ConfigureAwait(false); }
        else
        {
            await HandsFreeVoiceContinuation.JoinReplyCaptureAsync(Capture, released, Retire).ConfigureAwait(false);
        }
        generation = released;
        Volatile.Write(ref handover, 0);
        if (!IsCurrent) { Retire(); }
    }

    public void Retire() => Interlocked.Exchange(ref retired, 1);

    internal void ReleaseOwner()
    {
        Retire();
        wake?.Retire();
    }
}
