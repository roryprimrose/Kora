using System.Text;

namespace Kora.Core.Context;

public sealed class LocalFileLexicalRetrieval : ILocalFileRetrieval
{
    private sealed record Chunk(int Start, int Length, string? Heading);
    private sealed record Match(Chunk Chunk, int Terms, int Frequency);

    public LocalFileSearchResult Search(LocalFileRevision revision, LocalFileReference exactSource,
        string query, DateTimeOffset observedAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(exactSource);
        cancellationToken.ThrowIfCancellationRequested();
        if (revision.Reference != exactSource) { return LocalFileSearchResult.Empty(LocalFileSearchOutcome.Stale, observedAt); }
        if (!LocalFileRetrievalPolicy.TryQuery(query, out var terms))
        {
            return LocalFileSearchResult.Empty(LocalFileSearchOutcome.InvalidQuery, observedAt);
        }
        var text = revision.Text;
        var matches = new List<Match>(LocalFileRetrievalPolicy.MaximumCitations);
        var matchingChunks = 0;
        foreach (var chunk in Chunks(text, cancellationToken))
        {
            var frequencies = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var word in LocalFileRetrievalPolicy.Tokens(TokenText(text, chunk), cancellationToken))
            {
                if (terms.Contains(word))
                {
                    frequencies[word] = Math.Min(LocalFileRetrievalPolicy.MaximumTermFrequency,
                        frequencies.GetValueOrDefault(word) + 1);
                }
            }
            if (frequencies.Count != 0)
            {
                matchingChunks++;
                matches.Add(new(chunk, frequencies.Count, frequencies.Values.Sum()));
                matches.Sort(Compare);
                if (matches.Count > LocalFileRetrievalPolicy.MaximumCitations) { matches.RemoveAt(matches.Count - 1); }
            }
        }
        // OR retrieval: unique query terms, then saturated frequency, then exact source offset.
        var citations = new List<LocalFileCitation>();
        var bytes = 0;
        var identity = revision.Review.Metadata.CanonicalPath.Split('\\')[^1];
        foreach (var match in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = match.Chunk;
            var excerpt = text.Substring(chunk.Start, chunk.Length);
            var size = Encoding.UTF8.GetByteCount(excerpt);
            if (bytes + size > LocalFileRetrievalPolicy.MaximumExcerptUtf8Bytes) { break; }
            bytes += size;
            var start = Location(text, chunk.Start);
            var end = Location(text, chunk.Start + chunk.Length);
            citations.Add(new(revision.Reference, identity, chunk.Start, chunk.Length,
                start.Line, start.Column, end.Line, end.Column, chunk.Heading, excerpt, match.Terms, match.Frequency));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(matchingChunks == 0 ? LocalFileSearchOutcome.NoMatch : LocalFileSearchOutcome.Matched,
            observedAt, citations.AsReadOnly(), matchingChunks, citations.Count < matchingChunks);
    }

    private static int Compare(Match left, Match right)
    {
        var terms = right.Terms.CompareTo(left.Terms);
        if (terms != 0) { return terms; }
        var frequency = right.Frequency.CompareTo(left.Frequency);
        return frequency != 0 ? frequency : left.Chunk.Start.CompareTo(right.Chunk.Start);
    }

    private static string TokenText(string text, Chunk chunk)
    {
        var start = chunk.Start;
        var end = start + chunk.Length;
        if (start > 0 && WordCharacter(RuneBefore(text, start)) && WordCharacter(Rune.GetRuneAt(text, start)))
        {
            while (start < end && WordCharacter(Rune.GetRuneAt(text, start)))
            {
                start += Rune.GetRuneAt(text, start).Utf16SequenceLength;
            }
        }
        if (end < text.Length && WordCharacter(RuneBefore(text, end)) && WordCharacter(Rune.GetRuneAt(text, end)))
        {
            while (end > start && WordCharacter(RuneBefore(text, end))) { end -= RuneBefore(text, end).Utf16SequenceLength; }
        }
        return text.Substring(start, end - start);
    }

    private static Rune RuneBefore(string text, int offset) =>
        Rune.GetRuneAt(text, char.IsLowSurrogate(text[offset - 1]) ? offset - 2 : offset - 1);

    private static bool WordCharacter(Rune character) => Rune.IsLetterOrDigit(character)
        || Rune.GetUnicodeCategory(character) is System.Globalization.UnicodeCategory.NonSpacingMark
            or System.Globalization.UnicodeCategory.SpacingCombiningMark or System.Globalization.UnicodeCategory.EnclosingMark;

    private static IEnumerable<Chunk> Chunks(string text, CancellationToken token)
    {
        var start = 0;
        var offset = 0;
        var lines = 0;
        string? heading = null;
        while (offset < text.Length)
        {
            token.ThrowIfCancellationRequested();
            var lineEnd = offset;
            while (lineEnd < text.Length && text[lineEnd] is not ('\r' or '\n')) { lineEnd++; }
            var end = lineEnd;
            if (end < text.Length)
            {
                end++;
                if (text[lineEnd] == '\r' && end < text.Length && text[end] == '\n') { end++; }
            }
            var line = text.AsSpan(offset, lineEnd - offset).Trim();
            var isHeading = IsHeading(line);
            var blank = line.IsEmpty;
            string? nextHeading = null;
            if (isHeading)
            {
                var length = Math.Min(line.Length, LocalFileRetrievalPolicy.MaximumTermCharacters);
                if (length < line.Length && char.IsHighSurrogate(line[length - 1])) { length--; }
                nextHeading = line[..length].ToString();
            }
            if (isHeading && offset > start) { yield return new(start, offset - start, heading); start = offset; lines = 0; }
            if (isHeading) { heading = nextHeading; }
            while (end - start > LocalFileRetrievalPolicy.MaximumChunkCharacters)
            {
                if (offset > start)
                {
                    yield return new(start, offset - start, heading);
                    start = offset;
                    lines = 0;
                }
                else
                {
                    var cut = SafeCut(text, start, LocalFileRetrievalPolicy.MaximumChunkCharacters);
                    yield return new(start, cut - start, heading);
                    start = offset = cut;
                }
            }
            offset = end;
            lines++;
            if (blank || lines == LocalFileRetrievalPolicy.MaximumChunkLines)
            {
                yield return new(start, offset - start, heading);
                start = offset;
                lines = 0;
            }
        }
        if (start < text.Length) { yield return new(start, text.Length - start, heading); }
    }

    private static bool IsHeading(ReadOnlySpan<char> line)
    {
        var level = 0;
        while (level < line.Length && line[level] == '#') { level++; }
        return level is >= 1 and <= 6 && level < line.Length && line[level] is ' ' or '\t';
    }

    private static int SafeCut(string text, int start, int maximum)
    {
        var cut = start + maximum;
        // Admission guarantees paired surrogates; a CR at a long-line cut belongs to CRLF.
        if (char.IsHighSurrogate(text[cut - 1])) { cut--; }
        if (text[cut - 1] == '\r') { cut--; }
        // Prefer whitespace/punctuation/scalar delimiters; never split an ordinary bounded term.
        var lower = start + maximum / 2;
        for (var at = cut; at > lower; at--)
        {
            if (!char.IsLowSurrogate(text[at]) && !WordCharacter(RuneBefore(text, at))) { return at; }
        }
        return cut;
    }

    // Locations use one-based UTF-16 columns; end is exclusive, CRLF is one newline.
    private static (int Line, int Column) Location(string text, int target)
    {
        var line = 1;
        var column = 1;
        for (var at = 0; at < target; at++)
        {
            if (text[at] == '\r')
            {
                line++;
                column = 1;
                if (at + 1 < target && text[at + 1] == '\n') { at++; }
            }
            else if (text[at] == '\n') { line++; column = 1; }
            else { column++; }
        }
        return (line, column);
    }
}
