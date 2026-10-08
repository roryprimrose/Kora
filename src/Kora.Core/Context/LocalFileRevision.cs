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
}
