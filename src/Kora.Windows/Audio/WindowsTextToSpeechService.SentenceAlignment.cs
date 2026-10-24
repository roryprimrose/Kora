using System.Speech.Synthesis;
using Kora.Core.Voice;

namespace Kora.Windows.Audio;

public sealed partial class WindowsTextToSpeechService
{
    private SentenceAlignment? sentenceAlignment;

    private void OnSpeakProgress(object? sender, SpeakProgressEventArgs args)
    {
        lock (stateLock)
        {
            if (!stopRequested && ReferenceEquals(activePrompt, args.Prompt))
            {
                sentenceAlignment?.Observe(args.Prompt, playbackGeneration,
                    args.CharacterPosition, args.CharacterCount, args.AudioPosition);
            }
        }
    }

    // No exact Windows voice/rate/output profile has enablement evidence yet.
    // Native word events are implementation evidence, not profile qualification.
    private static bool IsSentenceAlignmentProfileQualified() => false;

    internal sealed class SentenceAlignment(Prompt prompt, long generation, string source)
    {
        private readonly SpeechSentenceMap? map = SpeechSentenceMap.Create(source);
        private readonly bool[] covered = new bool[Math.Min(source.Length, SpeechSentenceMap.MaximumCharacters)];
        private readonly Dictionary<int, TimeSpan> starts = [];
        private TimeSpan lastTime;
        private int lastStart = -1;
        private int lastEnd = -1;
        private int records;
        private bool invalid;
        private bool closed;
        private bool sealedTimeline;
        private TimeSpan duration;
        public bool Available { get; private set; }

        public void Observe(Prompt observedPrompt, long observedGeneration, int start, int count, TimeSpan audioPosition)
        {
            if (closed || !ReferenceEquals(prompt, observedPrompt) || generation != observedGeneration) { return; }
            if (invalid || map is null) { invalid = true; return; }
            if (++records > SpeechSentenceMap.MaximumCharacters * 2
                || start < 0 || count <= 0 || start > source.Length - count
                || !map.IsCharacterBoundary(start) || !map.IsCharacterBoundary(start + count)
                || audioPosition < TimeSpan.Zero || audioPosition < lastTime
                || start < lastStart || start == lastStart && start + count != lastEnd
                || start > lastStart && start < lastEnd)
            {
                invalid = true;
                return;
            }
            var sentence = map.Sentences.First(range => start >= range.Start && start < range.Start + range.Length);
            if (start + count > sentence.Start + sentence.Length
                || !Enumerable.Range(start, count).Any(index => RequiresCoverage(source[index])))
            {
                invalid = true;
                return;
            }
            starts.TryAdd(sentence.Index, audioPosition);
            Array.Fill(covered, true, start, count);
            lastStart = start;
            lastEnd = start + count;
            lastTime = audioPosition;
        }

        public void Close() => closed = true;

        public void Seal(TimeSpan audioDuration, bool profileQualified)
        {
            closed = true;
            if (sealedTimeline) { throw new InvalidOperationException("Sentence alignment is already sealed."); }
            sealedTimeline = true;
            duration = audioDuration;
            if (invalid || map is null || audioDuration <= TimeSpan.Zero || lastTime >= audioDuration
                || starts.Count != map.Sentences.Count) { return; }
            for (var index = 0; index < source.Length; index++)
            {
                if (RequiresCoverage(source[index]) && !covered[index]) { return; }
            }
            for (var index = 1; index < map.Sentences.Count; index++)
            {
                if (starts[index] <= starts[index - 1]) { return; }
            }
            Available = profileQualified;
        }

        private static bool RequiresCoverage(char character) => !char.IsWhiteSpace(character) && !char.IsPunctuation(character);

        public SpeechSentencePosition? At(TimeSpan devicePosition)
        {
            if (!Available || map is null || devicePosition < TimeSpan.Zero || devicePosition > duration) { return null; }
            for (var index = map.Sentences.Count - 1; index >= 0; index--)
            {
                var range = map.Sentences[index];
                if (starts[index] <= devicePosition)
                {
                    return new(index, range.Start, range.Length, starts[index], devicePosition, duration);
                }
            }
            return null;
        }
    }
}
