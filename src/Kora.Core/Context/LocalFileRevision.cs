using Kora.Core.Hosting;

namespace Kora.Core.Context;

public sealed class LocalFileRevision
{
    public LocalFileRevision(LocalFileReview review, ReadOnlySpan<byte> bytes, DateTimeOffset admittedAt)
    {
        ArgumentNullException.ThrowIfNull(review);
        LocalFilePolicy.ValidatePath(review.Metadata.CanonicalPath);
        if (review.ReviewId == Guid.Empty || review.SourceId == Guid.Empty
            || string.IsNullOrWhiteSpace(review.Metadata.FileIdentity)
            || review.Metadata.ByteLength != bytes.Length)
        {
            throw new InvalidDataException("The revision must match the exact reviewed source identity and byte length.");
        }
        Text = LocalFilePolicy.Decode(bytes);
        Digest = LocalFilePolicy.Digest(bytes);
        Review = review;
        RevisionId = Guid.NewGuid();
        ItemId = Guid.NewGuid();
        AdmittedAt = admittedAt;
    }

    public LocalFileReview Review { get; }
    public Guid RevisionId { get; }
    public Guid ItemId { get; }
    public string Text { get; }
    public string Digest { get; }
    public DateTimeOffset AdmittedAt { get; }
    public LocalFileReference Reference => new(Review.SourceId, RevisionId, ItemId, Digest);

    private LocalFileRevision(LocalFileRevision captured, LocalFileReference reference)
    {
        if (reference.SourceId != captured.Review.SourceId || reference.RevisionId == Guid.Empty
            || reference.ItemId == Guid.Empty || !string.Equals(reference.Digest, captured.Digest, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The retained revision does not match its exact original-byte lineage.");
        }
        Review = captured.Review;
        RevisionId = reference.RevisionId;
        ItemId = reference.ItemId;
        Text = captured.Text;
        Digest = captured.Digest;
        AdmittedAt = captured.AdmittedAt;
    }

    public static LocalFileRevision Restore(LocalFileReview review, ReadOnlySpan<byte> originalBytes,
        DateTimeOffset admittedAt, LocalFileReference reference) =>
        new(new LocalFileRevision(review, originalBytes, admittedAt), reference);
}
