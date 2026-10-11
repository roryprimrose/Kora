namespace Kora.Core.Context;

public sealed partial class LocalFileLexicalRetrieval
{
    public static LocalFileCitationSpan ResolveCitation(LocalFileRevision file, LocalFileCitation citation,
        LocalFileRetrievalMode mode)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(citation);
        var text = file.Text;
        if (!LocalFileRetrievalPolicy.TryVersion(mode, out _) || citation.Source != file.Reference
            || citation.Start < 0 || citation.Length <= 0 || citation.Start > text.Length
            || citation.Length > text.Length - citation.Start)
        {
            throw new InvalidDataException("The citation does not identify a bounded span of this exact admitted source.");
        }
        var end = citation.Start + citation.Length;
        if (!CitationBoundary(text, citation.Start) || !CitationBoundary(text, end)
            || !text.AsSpan(citation.Start, citation.Length).SequenceEqual(citation.Excerpt)
            || Location(text, citation.Start) != (citation.StartLine, citation.StartColumn)
            || Location(text, end) != (citation.EndLine, citation.EndColumn))
        {
            throw new InvalidDataException("The citation excerpt or original UTF-16 location is invalid.");
        }
        // Use the same chunk/heading policy as retrieval, including v1 labels and v2 authored heading spans.
        var chunks = mode == LocalFileRetrievalMode.MarkdownStructure
            && LocalFileRetrievalPolicy.IsMarkdown(file.Review.Metadata.CanonicalPath)
            ? MarkdownChunks(text, CancellationToken.None) : Chunks(text, CancellationToken.None);
        if (!chunks.Any(chunk => chunk.Start == citation.Start && chunk.Length == citation.Length
            && string.Equals(chunk.Heading, citation.Heading, StringComparison.Ordinal)
            && chunk.Fragment == citation.IsStructuralFragment && chunk.HeadingContext == citation.HeadingContext))
        {
            throw new InvalidDataException("The citation is not an exact chunk under its declared retrieval policy.");
        }
        return new(file, citation, text.Substring(citation.Start, citation.Length));
    }

    private static bool CitationBoundary(string text, int offset) => offset == 0 || offset == text.Length
        || !char.IsLowSurrogate(text[offset])
            && !(text[offset - 1] == '\r' && text[offset] == '\n');
}
