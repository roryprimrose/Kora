using System.Text;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Core.KnowledgeSources;

/// <summary>One unchanged fresh question and exact retained evidence; registration is never consent.</summary>
public sealed class KnowledgeSourceAnswerReview
{
    private int consumed;
    private int retired;

    public KnowledgeSourceAnswerReview(KnowledgeSource source, KnowledgeSourceRevision revision, string question,
        LocalFileSearchResult search, LocalContextSessionTarget target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(target);
        if (source.State != KnowledgeSourceState.Enabled || source.Id != revision.Reference.SourceId
            || !source.Folder.Matches(revision.Review.Metadata)
            || !LocalFileRetrievalPolicy.TryQuery(question, out _)
            || search.Outcome != LocalFileSearchOutcome.Matched
            || search.Mode != LocalFileRetrievalMode.LexicalLines
            || !string.Equals(search.PolicyVersion, LocalFileRetrievalPolicy.Version, StringComparison.Ordinal)
            || search.Citations.Count is < 1 or > LocalFileRetrievalPolicy.MaximumCitations
            || search.MatchingChunks < search.Citations.Count
            || search.Truncated != (search.MatchingChunks > search.Citations.Count)
            || target.SourceRequest.InvocationId is not null
            || target.Request.InvocationId is not null
            || target.SourceRequest.RequestId == revision.Review.Request.RequestId
            || target.SourceRequest.TaskId == revision.Review.Request.TaskId)
        { throw new InvalidDataException("Named answering requires a fresh original question and one exact enabled retained source."); }
        var spans = new HashSet<(LocalFileReference, int, int)>();
        var bytes = 0;
        foreach (var citation in search.Citations)
        {
            _ = revision.ResolveCitation(citation);
            bytes += Encoding.UTF8.GetByteCount(citation.Excerpt);
            if (!spans.Add((citation.Source, citation.Start, citation.Length))
                || bytes > LocalFileRetrievalPolicy.MaximumExcerptUtf8Bytes)
            { throw new InvalidDataException("Duplicate or excessive named evidence is not admitted."); }
        }
        Source = source;
        Revision = revision;
        Question = question;
        Search = search with { Citations = Array.AsReadOnly(search.Citations.ToArray()) };
        Target = target;
        ExcerptUtf8Bytes = bytes;
    }

    public KnowledgeSource Source { get; }
    public KnowledgeSourceRevision Revision { get; }
    public string Question { get; }
    public LocalFileSearchResult Search { get; }
    public LocalContextSessionTarget Target { get; }
    public int ExcerptUtf8Bytes { get; }
    public DateTimeOffset ExpiresAt => Search.ObservedAt.AddSeconds(30);
    public bool IsRevoked => Volatile.Read(ref retired) != 0;
    public bool IsCurrent(DateTimeOffset now) => Volatile.Read(ref retired) == 0
        && now >= Search.ObservedAt && now < ExpiresAt;
    public bool TryConsume() => Volatile.Read(ref retired) == 0 && Interlocked.Exchange(ref consumed, 1) == 0;
    public void Revoke() => Interlocked.Exchange(ref retired, 1);
}
