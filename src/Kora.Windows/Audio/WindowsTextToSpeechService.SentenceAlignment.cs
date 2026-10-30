using System.Speech.Synthesis;
using Kora.Core.Voice;

namespace Kora.Windows.Audio;

public sealed partial class WindowsTextToSpeechService
{
    private SentenceAlignment? sentenceAlignment;

    private void RetireSentenceAlignment()
    {
        sentenceAlignment?.Retire();
        sentenceAlignment = null;
    }

    private void OnSpeakProgress(object? sender, SpeakProgressEventArgs args)
    {
        lock (stateLock)
        {
            if (!stopRequested && currentPlaybackId is { } id && ReferenceEquals(activePrompt, args.Prompt))
            {
                sentenceAlignment?.Observe(args.Prompt, new(new(id, playbackGeneration),
                    args.CharacterPosition, args.CharacterCount, args.AudioPosition));
            }
        }
    }

    // No exact Windows voice/rate/output profile has enablement evidence yet.
    // Native word events are implementation evidence, not profile qualification.
    private static bool IsSentenceAlignmentProfileQualified() => false;

    internal sealed class SentenceAlignment(Prompt prompt, SpeechPlaybackToken playback, string source)
    {
        private string sourceText = source;
        private SpeechSentenceMap? map = SpeechSentenceMap.Create(source);
        private readonly bool[] covered = new bool[Math.Min(source.Length, SpeechSentenceMap.MaximumCharacters)];
        private readonly Dictionary<int, SpeechSourceProgress> starts = [];
        private TimeSpan lastTime;
        private int lastStart = -1;
        private int lastEnd = -1;
        private int records;
        private bool invalid;
        private bool closed;
        private bool sealedTimeline;
        private TimeSpan duration;
        public bool Available { get; private set; }

        public void Observe(Prompt observedPrompt, SpeechSourceProgress progress)
        {
            if (closed || !ReferenceEquals(prompt, observedPrompt) || playback != progress.Playback) { return; }
            if (invalid || map is null) { invalid = true; return; }
            var start = progress.CharacterPosition;
            var count = progress.CharacterCount;
            var audioPosition = progress.AudioPosition;
            var sentence = map.Resolve(progress);
            if (++records > SpeechSentenceMap.MaximumCharacters * 2
                || playback.Id == Guid.Empty || playback.Generation < 0 || sentence is null
                || audioPosition < TimeSpan.Zero || audioPosition < lastTime
                || start < lastStart || start == lastStart && start + count != lastEnd
                || start > lastStart && start < lastEnd)
            {
                invalid = true;
                return;
            }
            if (!Enumerable.Range(start, count).Any(index => RequiresCoverage(sourceText[index])))
            {
                invalid = true;
                return;
            }
            starts.TryAdd(sentence.Index, progress);
            Array.Fill(covered, true, start, count);
            lastStart = start;
            lastEnd = start + count;
            lastTime = audioPosition;
        }

        public void Close() => closed = true;

        public void Retire()
        {
            closed = true;
            Available = false;
            map = null;
            sourceText = string.Empty;
            starts.Clear();
            Array.Clear(covered);
        }

        public void Seal(TimeSpan audioDuration, bool profileQualified)
        {
            closed = true;
            if (sealedTimeline) { throw new InvalidOperationException("Sentence alignment is already sealed."); }
            sealedTimeline = true;
            duration = audioDuration;
            if (invalid || map is null || audioDuration <= TimeSpan.Zero || lastTime >= audioDuration
                || starts.Count != map.Sentences.Count) { return; }
            for (var index = 0; index < sourceText.Length; index++)
            {
                if (RequiresCoverage(sourceText[index]) && !covered[index]) { return; }
            }
            for (var index = 1; index < map.Sentences.Count; index++)
            {
                if (starts[index].AudioPosition <= starts[index - 1].AudioPosition) { return; }
            }
            Available = profileQualified;
        }

        private static bool RequiresCoverage(char character) => !char.IsWhiteSpace(character) && !char.IsPunctuation(character);

        public SpeechSentencePosition? At(TimeSpan devicePosition)
        {
            if (!Available || map is null || devicePosition < TimeSpan.Zero || devicePosition > duration) { return null; }
            for (var index = map.Sentences.Count - 1; index >= 0; index--)
            {
                if (starts[index].AudioPosition <= devicePosition)
                {
                    return new(starts[index], devicePosition, duration);
                }
            }
            return null;
        }
    }
}
