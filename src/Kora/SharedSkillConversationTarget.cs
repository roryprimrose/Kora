using Kora.Core.Storage;

namespace Kora;

internal sealed record SharedSkillConversationTarget(SessionWorkspaceEntry Entry)
{
    public override string ToString() => $"{Entry.Metadata?.Name} | {Entry.Authority.SessionId.Value:D} | generation {Entry.Authority.Generation.Value}";
}
