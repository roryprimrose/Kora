using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Core.Presentation;

/// <summary>Immutable provenance for the one read-only Details question, not original-task authority.</summary>
public sealed record DetailsOfferBinding(
    HostRequest OriginalRequest, HostRevision Generation, long MetadataRevision,
    long Sequence, long MessageRevision, Guid Item, string SourceDigest,
    Guid OptIn, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, Guid RunId = default)
{
    public const string Purpose = "details-offer-v1";
    public static QuestionSpec CreateSpec() => new(
        "Open the full Details for this exact completed answer?",
        QuestionKind.SingleChoice, [new("yes", "Yes"), new("not-now", "Not now")],
        purpose: Purpose, sourceId: "host-details");

    public void Validate(HostQuestionRecord question)
    {
        if (question.Key is null || question.Spec is null
            || OriginalRequest is null || OriginalRequest.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || OriginalRequest.InvocationId is not null
            || Generation.Value <= 0 || MetadataRevision < 0 || Sequence is < 1 or > 100 || MessageRevision != 3
            || Item == Guid.Empty || OptIn == Guid.Empty || RunId == Guid.Empty
            || SourceDigest is null || SourceDigest.Length != 64 || SourceDigest.Any(c => !char.IsAsciiHexDigit(c))
            || CreatedAt.Offset != TimeSpan.Zero || ExpiresAt.Offset != TimeSpan.Zero
            || ExpiresAt <= CreatedAt || ExpiresAt > CreatedAt.AddSeconds(60)
            || question.Key.Request.Origin != RequestOrigin.HostSystem
            || question.Key.Request.InvocationId is not null
            || question.Key.Request.SessionId != OriginalRequest.SessionId
            || question.Key.Request.RequestId == OriginalRequest.RequestId
            || question.Key.Request.TaskId == OriginalRequest.TaskId
            || question.SessionGeneration != Generation || question.ExpiresAt != ExpiresAt
            || question.Proposal is not null || !question.Spec.Matches(CreateSpec()))
        { throw new InvalidDataException("The exact source-bound Details presentation authority is invalid."); }
    }
}