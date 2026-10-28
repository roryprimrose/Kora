using System.Text;
using Kora.Core.Dependencies;

namespace Kora.Core.Context.FolderAnswers;

/// <summary>A volatile proposal, not source-use or durable session authority.</summary>
public sealed class FolderAnswerReview
{
    private int consumed;

    public FolderAnswerReview(LocalFolderRevision source, string question, LocalFileSearchResult search,
        LocalContextSessionTarget target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(target);
        if (!LocalFileRetrievalPolicy.TryQuery(question, out _)
            || search.Outcome != LocalFileSearchOutcome.Matched
            || search.Mode != LocalFileRetrievalMode.LexicalLines
            || !string.Equals(search.PolicyVersion, LocalFileRetrievalPolicy.Version, StringComparison.Ordinal)
            || search.Citations.Count is < 1 or > LocalFileRetrievalPolicy.MaximumCitations
            || search.MatchingChunks < search.Citations.Count
            || search.Truncated != (search.MatchingChunks > search.Citations.Count)
            || target.SourceRequest.SessionId != source.Review.Request.SessionId
            || target.SourceRequest.TaskId != source.Review.Request.TaskId
            || target.SourceRequest.Origin != Kora.Core.Hosting.RequestOrigin.LocalUi
            || target.SourceRequest.InvocationId is not null)
        {
            throw new InvalidDataException("The exact folder question, source lineage or lexical selection is not admitted.");
        }
        var spans = new HashSet<(LocalFileReference Source, int Start, int Length)>();
        var bytes = 0;
        foreach (var citation in search.Citations)
        {
            var file = source.Files.SingleOrDefault(file => file.Reference == citation.Source)
                ?? throw new InvalidDataException("A citation does not belong to the reviewed folder revision.");
            _ = LocalFileLexicalRetrieval.ResolveCitation(file, citation, search.Mode);
            bytes += Encoding.UTF8.GetByteCount(citation.Excerpt);
            if (!spans.Add((citation.Source, citation.Start, citation.Length))
                || bytes > LocalFileRetrievalPolicy.MaximumExcerptUtf8Bytes)
            {
                throw new InvalidDataException("Duplicate or excessive folder evidence is not admitted.");
            }
        }
        Source = source;
        Question = question;
        Search = search with { Citations = Array.AsReadOnly(search.Citations.ToArray()) };
        Target = target;
        ExcerptUtf8Bytes = bytes;
    }

    public LocalFolderRevision Source { get; }
    public string Question { get; }
    public LocalFileSearchResult Search { get; }
    public LocalContextSessionTarget Target { get; }
    public int ExcerptUtf8Bytes { get; }
    public bool TryConsume() => Interlocked.Exchange(ref consumed, 1) == 0;
}
