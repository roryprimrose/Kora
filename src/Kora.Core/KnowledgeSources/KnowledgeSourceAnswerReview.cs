using System.Text;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Core.KnowledgeSources;

/// <summary>One unchanged fresh question and exact retained evidence; registration is never consent.</summary>
public sealed class KnowledgeSourceAnswerReview
{
    private readonly Lock sync = new();
    private readonly KnowledgeSourceAnswerReview owner;
    private readonly long selectionVersion;
    private long currentSelection;
    private bool consumed;
    private bool retired;

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
        RetrievedSearch = Search;
        owner = this;
    }

    private KnowledgeSourceAnswerReview(KnowledgeSourceAnswerReview original, LocalFileSearchResult selected, long version)
        : this(original.Source, original.Revision, original.Question, selected, original.Target)
    {
        owner = original.owner;
        selectionVersion = version;
        RetrievedSearch = original.RetrievedSearch;
    }

    public KnowledgeSource Source { get; }
    public KnowledgeSourceRevision Revision { get; }
    public string Question { get; }
    public LocalFileSearchResult Search { get; }
    public LocalFileSearchResult RetrievedSearch { get; }
    public LocalContextSessionTarget Target { get; }
    public int ExcerptUtf8Bytes { get; }
    public DateTimeOffset ExpiresAt => RetrievedSearch.ObservedAt.AddSeconds(30);
    public bool IsRevoked { get { lock (owner.sync) { return owner.retired; } } }

    public KnowledgeSourceAnswerReview SelectEvidence(IReadOnlyList<LocalFileCitation> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        var citations = selected.Take(LocalFileRetrievalPolicy.MaximumCitations + 1).ToArray();
        lock (owner.sync)
        {
            owner.currentSelection++;
            if (owner.retired || owner.consumed || citations.Length is < 1 or > LocalFileRetrievalPolicy.MaximumCitations
                || citations.Any(citation => !RetrievedSearch.Citations.Any(existing => ReferenceEquals(existing, citation))))
            { throw new InvalidDataException("Choose a nonempty set of exact whole citations from this named-source retrieval."); }
            return new(this, RetrievedSearch with
            {
                Citations = Array.AsReadOnly(citations),
                Truncated = RetrievedSearch.MatchingChunks > citations.Length,
            }, owner.currentSelection);
        }
    }

    public bool IsFromSameRetrieval(KnowledgeSourceAnswerReview original) => ReferenceEquals(owner, original.owner);
    public void InvalidateSelection() { lock (owner.sync) { owner.currentSelection++; } }
    public void Revoke() { lock (owner.sync) { owner.retired = true; } }

    public bool IsCurrent(DateTimeOffset now)
    {
        lock (owner.sync)
        {
            return !owner.retired && selectionVersion == owner.currentSelection
                && now >= RetrievedSearch.ObservedAt && now < ExpiresAt;
        }
    }

    public bool TryConsume()
    {
        lock (owner.sync)
        {
            if (owner.retired || selectionVersion != owner.currentSelection || owner.consumed) { return false; }
            owner.consumed = true;
            return true;
        }
    }
}
