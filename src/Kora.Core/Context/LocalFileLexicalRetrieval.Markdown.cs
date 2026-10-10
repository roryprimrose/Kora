namespace Kora.Core.Context;

public sealed partial class LocalFileLexicalRetrieval
{
    private sealed record SourceLine(int Start, int ContentEnd, int End);
    private sealed record Section(int Start, int End, int Lines, LocalFileHeadingContext? Heading, bool Incomplete);

    private static IEnumerable<Chunk> MarkdownChunks(string text, CancellationToken token)
    {
        var sourceLines = MarkdownLines(text, token);
        var sections = new List<Section>();
        var boundaries = new SortedSet<int> { 0, text.Length };
        var sectionStart = 0;
        var sectionLine = 0;
        LocalFileHeadingContext? heading = null;
        var fence = '\0';
        var fenceLength = 0;
        var inlineTicks = 0;
        var container = false;
        var html = false;
        for (var index = 0; index < sourceLines.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var source = sourceLines[index];
            var line = text.AsSpan(source.Start, source.ContentEnd - source.Start);
            if (fence != '\0')
            {
                if (Fence(line, out var marker, out var length, out var tail)
                    && marker == fence && length >= fenceLength && tail.Trim(" \t").IsEmpty)
                {
                    fence = '\0';
                    boundaries.Add(source.End);
                }
                continue;
            }
            if (line.Trim(" \t").IsEmpty)
            {
                boundaries.Add(source.Start);
                boundaries.Add(source.End);
                container = html = false;
                continue;
            }
            var indent = LeadingSpaces(line);
            var content = line[indent..];
            var suppressed = indent > 3 || container || html || inlineTicks != 0;
            if (!suppressed && Fence(line, out var opener, out var openingLength, out var info)
                && (opener != '`' || !info.Contains('`')))
            {
                boundaries.Add(source.Start);
                fence = opener;
                fenceLength = openingLength;
                continue;
            }
            var setext = !suppressed && boundaries.Contains(source.Start) && IsSetextCandidate(content) && index + 1 < sourceLines.Count
                && Setext(text.AsSpan(sourceLines[index + 1].Start,
                    sourceLines[index + 1].ContentEnd - sourceLines[index + 1].Start));
            if (!suppressed && (Atx(content) || setext))
            {
                if (source.Start > sectionStart) { sections.Add(new(sectionStart, source.Start, index - sectionLine, heading, false)); }
                sectionStart = source.Start;
                sectionLine = index;
                var labelLength = Math.Min(content.Length, LocalFileRetrievalPolicy.MaximumTermCharacters);
                if (labelLength < content.Length && char.IsHighSurrogate(content[labelLength - 1])) { labelLength--; }
                heading = new(source.Start + indent, labelLength, index + 1, indent + 1, content[..labelLength].ToString());
                boundaries.Add(source.Start);
                if (setext) { index++; }
                boundaries.Add(sourceLines[index].End);
                continue;
            }
            // Containers/raw HTML and unfinished inline code are conservative opaque text, not section authority.
            if (Container(content)) { container = true; }
            if (content.StartsWith("<", StringComparison.Ordinal)) { html = true; }
            UpdateInlineTicks(line, ref inlineTicks);
        }
        if (sectionStart < text.Length)
        {
            sections.Add(new(sectionStart, text.Length, sourceLines.Count - sectionLine, heading, fence != '\0' || inlineTicks != 0 || html));
        }
        foreach (var section in sections)
        {
            token.ThrowIfCancellationRequested();
            if (section.End - section.Start <= LocalFileRetrievalPolicy.MaximumChunkCharacters
                && section.Lines <= LocalFileRetrievalPolicy.MaximumChunkLines)
            {
                yield return new(section.Start, section.End - section.Start, section.Heading?.Label,
                    section.Incomplete, section.Heading);
                continue;
            }
            var start = section.Start;
            foreach (var end in boundaries.GetViewBetween(section.Start + 1, section.End))
            {
                foreach (var chunk in MarkdownFragments(text, start, end, section.Heading, token)) { yield return chunk; }
                start = end;
            }
        }
    }

    private static List<SourceLine> MarkdownLines(string text, CancellationToken token)
    {
        var lines = new List<SourceLine>();
        var offset = 0;
        while (offset < text.Length)
        {
            token.ThrowIfCancellationRequested();
            var start = offset;
            while (offset < text.Length && text[offset] is not ('\r' or '\n'))
            {
                token.ThrowIfCancellationRequested();
                offset++;
            }
            var contentEnd = offset;
            if (offset < text.Length)
            {
                offset++;
                if (text[contentEnd] == '\r' && offset < text.Length && text[offset] == '\n') { offset++; }
            }
            lines.Add(new(start, contentEnd, offset));
        }
        return lines;
    }

    private static IEnumerable<Chunk> MarkdownFragments(string text, int start, int end,
        LocalFileHeadingContext? heading, CancellationToken token)
    {
        while (start < end)
        {
            token.ThrowIfCancellationRequested();
            var offset = start;
            var lines = 0;
            while (offset < end && lines < LocalFileRetrievalPolicy.MaximumChunkLines)
            {
                var next = offset;
                while (next < end && text[next] is not ('\r' or '\n')) { token.ThrowIfCancellationRequested(); next++; }
                if (next < end)
                {
                    next++;
                    if (text[next - 1] == '\r' && next < end && text[next] == '\n') { next++; }
                }
                if (next - start > LocalFileRetrievalPolicy.MaximumChunkCharacters)
                {
                    if (offset == start) { offset = SafeCut(text, start, LocalFileRetrievalPolicy.MaximumChunkCharacters); }
                    break;
                }
                offset = next;
                lines++;
            }
            yield return new(start, offset - start, heading?.Label, true, heading);
            start = offset;
        }
    }

    private static int LeadingSpaces(ReadOnlySpan<char> line)
    {
        var indent = 0;
        while (indent < line.Length && line[indent] == ' ') { indent++; }
        return indent;
    }

    private static bool Fence(ReadOnlySpan<char> line, out char marker, out int length, out ReadOnlySpan<char> tail)
    {
        marker = '\0';
        length = 0;
        tail = default;
        var indent = LeadingSpaces(line);
        if (indent > 3 || indent == line.Length || line[indent] is not ('`' or '~')) { return false; }
        marker = line[indent];
        while (indent + length < line.Length && line[indent + length] == marker) { length++; }
        tail = line[(indent + length)..];
        return length >= 3;
    }

    private static bool Atx(ReadOnlySpan<char> content)
    {
        var hashes = 0;
        while (hashes < content.Length && content[hashes] == '#') { hashes++; }
        return hashes is >= 1 and <= 6 && (hashes == content.Length || content[hashes] is ' ' or '\t');
    }

    private static bool Setext(ReadOnlySpan<char> line)
    {
        var indent = LeadingSpaces(line);
        if (indent > 3 || indent == line.Length || line[indent] is not ('=' or '-')) { return false; }
        var content = line[indent..].TrimEnd(" \t");
        foreach (var character in content) { if (character != content[0]) { return false; } }
        return true;
    }

    private static bool IsSetextCandidate(ReadOnlySpan<char> content) =>
        !Container(content) && !content.ContainsAny('`', '|', '<')
        && !content.StartsWith("#", StringComparison.Ordinal) && !Setext(content);

    private static bool Container(ReadOnlySpan<char> content)
    {
        if (content[0] == '>' || content[0] == '\t') { return true; }
        if (content.Length > 1 && content[0] is '-' or '+' or '*' && content[1] is ' ' or '\t') { return true; }
        var digits = 0;
        while (digits < content.Length && char.IsAsciiDigit(content[digits])) { digits++; }
        return digits is >= 1 and <= 9 && digits + 1 < content.Length
            && content[digits] is '.' or ')' && content[digits + 1] is ' ' or '\t';
    }

    private static void UpdateInlineTicks(ReadOnlySpan<char> line, ref int ticks)
    {
        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] != '`' || (index > 0 && line[index - 1] == '\\')) { continue; }
            var start = index;
            while (index + 1 < line.Length && line[index + 1] == '`') { index++; }
            var length = index - start + 1;
            if (ticks == 0) { ticks = length; }
            else if (ticks == length) { ticks = 0; }
        }
    }
}
