using Kora.Core.Context;

namespace Kora.Core.KnowledgeSources;

public sealed class KnowledgeSourceRevision
{
    public KnowledgeSourceRevision(KnowledgeSourceReview review, IEnumerable<LocalFileRevision> files,
        KnowledgeSourceReference? retainedReference = null)
    {
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(files);
        var items = files.ToArray();
        if (review.ReviewId == Guid.Empty || review.SourceId == Guid.Empty
            || items.Length != review.Metadata.Files.Count
            || items.Where((file, index) => file is null || file.Review.Metadata != review.Metadata.Files[index]
                || file.Review.ReviewId != review.ReviewId || file.Review.SourceId != review.SourceId
                || file.Review.Request != review.Request || file.Review.Cause != review.Cause).Any()
            || items.Select(file => file.Reference).Distinct().Count() != items.Length
            || (retainedReference is not null
                && (retainedReference.SourceId != review.SourceId || retainedReference.RevisionId == Guid.Empty)))
        { throw new InvalidDataException("A named revision requires every exact reviewed item and its original lineage."); }
        Review = review;
        Files = Array.AsReadOnly(items);
        Reference = retainedReference ?? new(review.SourceId, Guid.NewGuid());
    }

    public KnowledgeSourceReview Review { get; }
    public IReadOnlyList<LocalFileRevision> Files { get; }
    public KnowledgeSourceReference Reference { get; }

    public LocalFileCitationSpan ResolveCitation(LocalFileCitation citation)
    {
        ArgumentNullException.ThrowIfNull(citation);
        var file = Files.SingleOrDefault(item => item.Reference == citation.Source)
            ?? throw new InvalidDataException("The citation does not belong to this exact retained source.");
        if (!string.Equals(citation.DisplayIdentity,
            KnowledgeSourcePolicy.RelativePath(Review.Metadata.CanonicalPath, file.Review.Metadata.CanonicalPath), StringComparison.Ordinal))
        { throw new InvalidDataException("The citation location does not identify its exact root-relative item."); }
        return LocalFileLexicalRetrieval.ResolveCitation(file, citation, LocalFileRetrievalMode.LexicalLines);
    }
}
