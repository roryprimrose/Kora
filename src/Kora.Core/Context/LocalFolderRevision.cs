namespace Kora.Core.Context;

public sealed class LocalFolderRevision
{
    public LocalFolderRevision(LocalFolderReview review, IEnumerable<LocalFileRevision> files)
    {
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(files);
        var items = files.ToArray();
        if (review.ReviewId == Guid.Empty || review.SourceId == Guid.Empty
            || items.Length != review.Metadata.Files.Count
            || items.Where((file, index) => file is null || file.Review.Metadata != review.Metadata.Files[index]
                || file.Review.ReviewId != review.ReviewId || file.Review.SourceId != review.SourceId
                || file.Review.Request != review.Request || file.Review.Cause != review.Cause).Any())
        {
            throw new InvalidDataException("The folder revision must contain every exact reviewed item in canonical order.");
        }
        Review = review;
        Files = Array.AsReadOnly(items);
        Reference = new(review.SourceId, Guid.NewGuid());
    }

    public LocalFolderReview Review { get; }
    public IReadOnlyList<LocalFileRevision> Files { get; }
    public LocalFolderReference Reference { get; }
}
