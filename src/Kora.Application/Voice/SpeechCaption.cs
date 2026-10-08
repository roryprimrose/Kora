using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.Voice;

// Text is local ephemeral presentation data: no serialization, logging or durable identity authority.
internal sealed class SpeechCaption
{
    private readonly Lock gate = new();
    private Guid playbackId;
    private Guid responseId;
    private HostRequest? request;
    private string? text;
    private Func<bool>? eligible;
    private long? generation;
    private bool observedPlaying;
    internal HostRequest? Source => request;

    public Guid Bind(Guid response, HostRequest hostRequest, string exactText, Func<bool> remainsEligible)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exactText);
        lock (gate)
        {
            Retire();
            responseId = response;
            request = hostRequest;
            text = exactText;
            eligible = remainsEligible;
            playbackId = Guid.NewGuid();
            return playbackId;
        }
    }

    public string? Observe(SpeechPlaybackFrame frame, Guid response)
    {
        Guid source;
        Func<bool>? admission;
        lock (gate)
        {
            source = playbackId;
            admission = eligible;
        }
        // Configuration/lifecycle callbacks can retire this source; never invoke them under its lock.
        var remainsEligible = admission?.Invoke() == true;
        lock (gate)
        {
            if (source != playbackId) { return null; }
            if (text is null || response != responseId || !remainsEligible)
            {
                Retire();
                return null;
            }
            if (!frame.IsPlaying)
            {
                if (observedPlaying) { Retire(); }
                return null;
            }
            if (frame.PlaybackId != playbackId || frame.Segment != 0
                || generation is { } bound && bound != frame.Generation)
            {
                Retire();
                return null;
            }
            generation = frame.Generation;
            observedPlaying = true;
            return text;
        }
    }

    public void Retire()
    {
        lock (gate)
        {
            text = null;
            request = null;
            eligible = null;
            generation = null;
            observedPlaying = false;
            playbackId = Guid.Empty;
        }
    }
}
