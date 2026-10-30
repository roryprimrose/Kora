using Kora.Core.Hosting;

namespace Kora.Core.Conversation;

/// <summary>Content-free host lineage for one whole selected same-session message.</summary>
public sealed record SubmittedHistorySource(HostId<RequestIdentity> Request, HostRevision Generation,
    long Sequence, long Revision, ConversationHistoryRole Role)
{
    public void Validate()
    {
        Request.Validate();
        if (Generation.Value <= 0 || Sequence is <= 0 or > ConversationText.MaximumSessionMessages
            || Revision is < 2 or > 3 || !Enum.IsDefined(Role))
        { throw new InvalidDataException("Submitted history lineage is invalid."); }
    }
}
