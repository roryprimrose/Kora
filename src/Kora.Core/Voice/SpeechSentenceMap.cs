namespace Kora.Core.Voice;

// Version 1 deliberately supports bounded plain text, not linguistic or SSML inference.
public sealed class SpeechSentenceMap
{
    public const int Version = 1;
    public const int MaximumCharacters = 32768;
    public const int MaximumSentences = 256;
    private readonly string source;
    public IReadOnlyList<SentenceRange> Sentences { get; }

    private SpeechSentenceMap(string source, List<SentenceRange> sentences)
    {
        this.source = source;
        Sentences = sentences.AsReadOnly();
    }

    public static SpeechSentenceMap? Create(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(source) || source.Length > MaximumCharacters) { return null; }
        for (var index = 0; index < source.Length; index++)
        {
            if (char.IsHighSurrogate(source[index]))
            {
                if (++index == source.Length || !char.IsLowSurrogate(source[index])) { return null; }
            }
            else if (char.IsLowSurrogate(source[index]) || char.IsControl(source[index]) && !char.IsWhiteSpace(source[index]))
            {
                return null;
            }
        }

        var sentences = new List<SentenceRange>();
        var start = 0;
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] is not ('.' or '!' or '?')) { continue; }
            var end = index + 1;
            while (end < source.Length && source[end] is '.' or '!' or '?' or '"' or '\'' or ')' or ']') { end++; }
            if (end < source.Length && !char.IsWhiteSpace(source[end])) { continue; }
            // Periods after initials/abbreviations are ambiguous; do not invent a sentence boundary.
            if (source[index] == '.')
            {
                var wordStart = index;
                while (wordStart > start && char.IsLetter(source[wordStart - 1])) { wordStart--; }
                var word = source[wordStart..index];
                if (word.Length == 1 || IsAbbreviation(word)) { return null; }
            }
            while (end < source.Length && char.IsWhiteSpace(source[end])) { end++; }
            if (!AddSentence(sentences, start, end)) { return null; }
            start = end;
            index = end - 1;
        }
        if (start < source.Length && !AddSentence(sentences, start, source.Length)) { return null; }
        return new(source, sentences);
    }

    private static bool IsAbbreviation(string word) =>
        word.Equals("Dr", StringComparison.OrdinalIgnoreCase)
        || word.Equals("Mr", StringComparison.OrdinalIgnoreCase)
        || word.Equals("Mrs", StringComparison.OrdinalIgnoreCase)
        || word.Equals("Ms", StringComparison.OrdinalIgnoreCase)
        || word.Equals("Prof", StringComparison.OrdinalIgnoreCase)
        || word.Equals("etc", StringComparison.OrdinalIgnoreCase)
        || word.Equals("vs", StringComparison.OrdinalIgnoreCase);

    private static bool AddSentence(List<SentenceRange> sentences, int start, int end)
    {
        if (sentences.Count == MaximumSentences) { return false; }
        sentences.Add(new(sentences.Count, start, end - start));
        return true;
    }

    public bool IsCharacterBoundary(int position) => position >= 0 && position <= source.Length
        && (position == source.Length || !char.IsLowSurrogate(source[position]));

    public string GetText(SentenceRange sentence)
    {
        ArgumentNullException.ThrowIfNull(sentence);
        if (sentence.Index < 0 || sentence.Index >= Sentences.Count || Sentences[sentence.Index] != sentence)
        {
            throw new ArgumentOutOfRangeException(nameof(sentence));
        }
        return source.Substring(sentence.Start, sentence.Length);
    }

    public SentenceRange? Resolve(SpeechSourceProgress progress)
    {
        var start = progress.CharacterPosition;
        var count = progress.CharacterCount;
        if (start < 0 || count <= 0 || start > source.Length - count
            || !IsCharacterBoundary(start) || !IsCharacterBoundary(start + count))
        {
            return null;
        }
        return Sentences.FirstOrDefault(range => start >= range.Start
            && start + count <= range.Start + range.Length);
    }

    public bool Matches(SpeechSentencePosition position) =>
        position.Source is { } progress && progress.Playback.Id != Guid.Empty && progress.Playback.Generation >= 0
        && Resolve(progress) is not null
        && progress.AudioPosition >= TimeSpan.Zero && progress.AudioPosition <= position.PlaybackPosition
        && position.PlaybackPosition <= position.AudioDuration && progress.AudioPosition < position.AudioDuration;
    public sealed record SentenceRange(int Index, int Start, int Length);
}
