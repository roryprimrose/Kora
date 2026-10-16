using System.Runtime.InteropServices;

using Kora.Core.Storage;

namespace Kora.Core.Conversation;

/// <summary>Passive inclusive original-message metadata bounds; null means Any.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ConversationRangeFilter(long? MinimumSequence = null, long? MaximumSequence = null,
    DateTimeOffset? SubmittedFrom = null, DateTimeOffset? SubmittedThrough = null)
{
    public void Validate()
    {
        if (MinimumSequence is < 1 or > ConversationText.MaximumSessionMessages
            || MaximumSequence is < 1 or > ConversationText.MaximumSessionMessages
            || MinimumSequence > MaximumSequence
            || SubmittedFrom is { Offset: var lowerOffset } && lowerOffset != TimeSpan.Zero
            || SubmittedThrough is { Offset: var upperOffset } && upperOffset != TimeSpan.Zero
            || SubmittedFrom > SubmittedThrough)
        { throw new InvalidDataException("Conversation ranges require ordered sequence bounds from 1 through the session message limit and UTC submitted times."); }
    }

    public bool Matches(ConversationMessage message) =>
        (MinimumSequence is not { } minimum || message.Sequence >= minimum)
        && (MaximumSequence is not { } maximum || message.Sequence <= maximum)
        && (SubmittedFrom is not { } from || message.SubmittedAt >= from)
        && (SubmittedThrough is not { } through || message.SubmittedAt <= through);

    public static ConversationRangeFilter Parse(string minimumSequence, string maximumSequence,
        string submittedFrom, string submittedThrough)
    {
        var filter = new ConversationRangeFilter(OptionalUnsignedDecimal.Parse(minimumSequence),
            OptionalUnsignedDecimal.Parse(maximumSequence), OptionalCanonicalUtc.Parse(submittedFrom),
            OptionalCanonicalUtc.Parse(submittedThrough));
        filter.Validate();
        return filter;
    }
}
