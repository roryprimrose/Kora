using System.Runtime.InteropServices;

using Kora.Core.Storage;

namespace Kora.Core.Conversation;

/// <summary>Passive inclusive original-message metadata bounds; null means Any.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ConversationRangeFilter(long? MinimumSequence = null, long? MaximumSequence = null,
    DateTimeOffset? SubmittedFrom = null, DateTimeOffset? SubmittedThrough = null,
    long? MinimumGeneration = null, long? MaximumGeneration = null,
    long? MinimumRevision = null, long? MaximumRevision = null)
{
    public void Validate()
    {
        if (MinimumSequence is < 1 or > ConversationText.MaximumSessionMessages
            || MaximumSequence is < 1 or > ConversationText.MaximumSessionMessages
            || MinimumSequence > MaximumSequence
            || SubmittedFrom is { Offset: var lowerOffset } && lowerOffset != TimeSpan.Zero
            || SubmittedThrough is { Offset: var upperOffset } && upperOffset != TimeSpan.Zero
            || SubmittedFrom > SubmittedThrough
            || MinimumGeneration is { } minimumGeneration && !ConversationMessageProvenance.IsValidGeneration(minimumGeneration)
            || MaximumGeneration is { } maximumGeneration && !ConversationMessageProvenance.IsValidGeneration(maximumGeneration)
            || MinimumGeneration > MaximumGeneration
            || MinimumRevision is { } minimumRevision && !ConversationMessageProvenance.IsValidRevision(minimumRevision)
            || MaximumRevision is { } maximumRevision && !ConversationMessageProvenance.IsValidRevision(maximumRevision)
            || MinimumRevision > MaximumRevision)
        { throw new InvalidDataException("Conversation ranges require ordered valid message sequence, positive generation, revision 1-3 and UTC submitted bounds."); }
    }

    public bool Matches(ConversationMessage message) =>
        (MinimumSequence is not { } minimum || message.Sequence >= minimum)
        && (MaximumSequence is not { } maximum || message.Sequence <= maximum)
        && (SubmittedFrom is not { } from || message.SubmittedAt >= from)
        && (SubmittedThrough is not { } through || message.SubmittedAt <= through)
        && (MinimumGeneration is not { } minimumGeneration || message.Generation.Value >= minimumGeneration)
        && (MaximumGeneration is not { } maximumGeneration || message.Generation.Value <= maximumGeneration)
        && (MinimumRevision is not { } minimumRevision || message.Revision >= minimumRevision)
        && (MaximumRevision is not { } maximumRevision || message.Revision <= maximumRevision);

    public static ConversationRangeFilter Parse(string minimumSequence, string maximumSequence,
        string submittedFrom, string submittedThrough, string minimumGeneration = "", string maximumGeneration = "",
        string minimumRevision = "", string maximumRevision = "")
    {
        var filter = new ConversationRangeFilter(OptionalUnsignedDecimal.Parse(minimumSequence),
            OptionalUnsignedDecimal.Parse(maximumSequence), OptionalCanonicalUtc.Parse(submittedFrom),
            OptionalCanonicalUtc.Parse(submittedThrough), OptionalUnsignedDecimal.Parse(minimumGeneration),
            OptionalUnsignedDecimal.Parse(maximumGeneration), OptionalUnsignedDecimal.Parse(minimumRevision),
            OptionalUnsignedDecimal.Parse(maximumRevision));
        filter.Validate();
        return filter;
    }
}
