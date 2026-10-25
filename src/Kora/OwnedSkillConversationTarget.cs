using Kora.Core.Storage;

namespace Kora;

internal sealed record OwnedSkillConversationTarget(SessionWorkspaceEntry Entry)
{
    public override string ToString() => $"{Entry.Authority.SessionId.Value:D} | Active";
}
