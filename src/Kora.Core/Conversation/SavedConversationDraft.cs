using Kora.Core.Hosting;

namespace Kora.Core.Conversation;

public sealed record SavedConversationDraft(HostId<SessionIdentity> SessionId, long Revision, string? Body)
{
    public int Version { get; init; } = 1;
    public void Validate()
    {
        SessionId.Validate();
        if (Version != 1 || Revision < 0 || Revision == 0 && Body is not null)
        { throw new InvalidDataException("A saved conversation draft requires a canonical revision."); }
        if (Body is not null) { ConversationText.Validate(Body); }
    }
}
