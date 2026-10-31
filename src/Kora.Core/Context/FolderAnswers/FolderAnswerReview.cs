using System.Text;
using Kora.Core.Dependencies;

namespace Kora.Core.Context.FolderAnswers;

/// <summary>A volatile proposal, not source-use or durable session authority.</summary>
public sealed class FolderAnswerReview
{
    private readonly Lock sync = new();
    private readonly FolderAnswerReview owner;
    private readonly long selectionVersion;
    private long currentSelection;
    private bool consumed;
    private bool retired;

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
        RetrievedSearch = Search;
        owner = this;
    }

    private FolderAnswerReview(FolderAnswerReview original, LocalFileSearchResult selected, long version)
        : this(original.Source, original.Question, selected, original.Target)
    {
        owner = original.owner;
        selectionVersion = version;
        RetrievedSearch = original.RetrievedSearch;
        IsUserSelected = true;
    }

    public LocalFolderRevision Source { get; }
    public string Question { get; }
    public LocalFileSearchResult Search { get; }
    public LocalFileSearchResult RetrievedSearch { get; }
    public LocalContextSessionTarget Target { get; }
    public int ExcerptUtf8Bytes { get; }
    public bool IsUserSelected { get; }
    public DateTimeOffset ExpiresAt => RetrievedSearch.ObservedAt.AddSeconds(30);

    public FolderAnswerReview SelectEvidence(IReadOnlyList<LocalFileCitation> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        var citations = selected.Take(LocalFileRetrievalPolicy.MaximumCitations + 1).ToArray();
        lock (owner.sync)
        {
            owner.currentSelection++;
            if (owner.retired || owner.consumed || citations.Length is < 1 or > LocalFileRetrievalPolicy.MaximumCitations
                || citations.Any(citation => !RetrievedSearch.Citations.Any(existing => ReferenceEquals(existing, citation))))
            {
                throw new InvalidDataException("Choose a nonempty set of exact whole citations from this retrieval.");
            }
            return new(this, RetrievedSearch with
            {
                Citations = Array.AsReadOnly(citations),
                Truncated = RetrievedSearch.MatchingChunks > citations.Length,
            }, owner.currentSelection);
        }
    }

    public void InvalidateSelection()
    {
        lock (owner.sync) { owner.currentSelection++; }
    }

    public void Invalidate()
    {
        lock (owner.sync) { owner.retired = true; }
    }

    public bool IsCurrent(DateTimeOffset now)
    {
        lock (owner.sync)
        {
            return !owner.retired && selectionVersion == owner.currentSelection && now < ExpiresAt;
        }
    }

    public bool TryConsume()
    {
        lock (owner.sync)
        {
            if (!IsUserSelected || owner.retired || selectionVersion != owner.currentSelection || owner.consumed) { return false; }
            owner.consumed = true;
            return true;
        }
    }
}
