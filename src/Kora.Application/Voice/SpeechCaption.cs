using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.Voice;

// Text is local ephemeral presentation data: no serialization, logging or durable identity authority.
internal sealed class SpeechCaption(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly Lock gate = new();
    private Guid playbackId;
    private Guid responseId;
    private HostRequest? request;
    private string? text;
    private SpeechSentenceMap? sentenceMap;
    private bool sentenceMode;
    private string? observedText;
    private SpeechSentencePosition? lastSentence;
    private Func<bool>? eligible;
    private long? generation;
    private bool observedPlaying;
    private bool playbackEnded;
    private bool completed;
    private bool pinned;
    private long completedAt;
    private int delaySeconds;
    internal HostRequest? Source => request;
    public bool IsPinned { get { lock (gate) { return pinned; } } }
    public bool IsPreviousSpeech { get { lock (gate) { return completed && text is not null; } } }

    public Guid Bind(Guid response, HostRequest hostRequest, string exactText, Func<bool> remainsEligible,
        SpeechCaptionOptions? options = null, SpeechTextMode mode = SpeechTextMode.CurrentUtterance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exactText);
        lock (gate)
        {
            Retire();
            responseId = response;
            request = hostRequest;
            text = exactText;
            sentenceMode = mode == SpeechTextMode.CurrentSentence;
            sentenceMap = sentenceMode ? SpeechSentenceMap.Create(exactText) : null;
            eligible = remainsEligible;
            delaySeconds = (options ?? SpeechCaptionOptions.Default).DismissalDelaySeconds;
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
            if (completed)
            {
                if (frame.IsPlaying && (frame.PlaybackId != playbackId || frame.Generation != generation!.Value || frame.Segment != 0)
                    || !pinned && clock.GetElapsedTime(completedAt) >= TimeSpan.FromSeconds(delaySeconds))
                {
                    Retire();
                    return null;
                }
                return observedText;
            }
            if (!frame.IsPlaying)
            {
                if (observedPlaying) { playbackEnded = true; }
                return null;
            }
            if (playbackEnded || frame.PlaybackId != playbackId || frame.Segment != 0
                || generation is { } bound && bound != frame.Generation)
            {
                Retire();
                return null;
            }
            generation = frame.Generation;
            if (sentenceMode)
            {
                if (!frame.SentenceAlignmentAvailable || sentenceMap is null)
                {
                    Retire();
                    return null;
                }
                if (frame.Sentence is not { } sentence)
                {
                    if (lastSentence is not null) { Retire(); }
                    return null;
                }
                if (!sentenceMap.Matches(sentence)
                    || sentence.Source.Playback != new SpeechPlaybackToken(playbackId, frame.Generation))
                {
                    Retire();
                    return null;
                }
                var range = sentenceMap.Resolve(sentence.Source)!;
                var previousRange = lastSentence is { } prior ? sentenceMap.Resolve(prior.Source) : null;
                if (lastSentence is { } previous && (range.Index < previousRange!.Index
                        || sentence.PlaybackPosition < previous.PlaybackPosition
                        || sentence.AudioDuration != previous.AudioDuration
                        || range.Index == previousRange.Index && sentence.Source != previous.Source
                        || range.Index > previousRange.Index
                            && sentence.Source.AudioPosition <= previous.Source.AudioPosition))
                {
                    Retire();
                    return null;
                }
                lastSentence = sentence;
                observedText = sentenceMap.GetText(range);
            }
            else { observedText = text; }
            observedPlaying = true;
            return observedText;
        }
    }

    public void Complete()
    {
        lock (gate)
        {
            if (text is null || !observedPlaying) { Retire(); return; }
            completed = true;
            completedAt = clock.GetTimestamp();
        }
    }

    public bool SetPinned(bool value, Guid response, SpeechPlaybackFrame frame)
    {
        Guid source;
        lock (gate) { source = playbackId; }
        if (Observe(frame, response) is null) { return false; }
        lock (gate)
        {
            if (source != playbackId) { return false; }
            pinned = value;
            return true;
        }
    }

    public void Retire()
    {
        lock (gate)
        {
            text = null;
            sentenceMap = null;
            observedText = null;
            lastSentence = null;
            sentenceMode = false;
            request = null;
            eligible = null;
            generation = null;
            observedPlaying = false;
            playbackEnded = false;
            completed = false;
            pinned = false;
            playbackId = Guid.Empty;
        }
    }
}
