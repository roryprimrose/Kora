using Kora.Core.Storage;

namespace Kora;

internal sealed record PromptConversationTarget(SessionWorkspaceEntry Entry)
{
    public override string ToString() => $"Session {Entry.Authority.SessionId.Value:D} | name: {Entry.Metadata?.Name.Value ?? "Unnamed"} "
        + $"| generation {Entry.Authority.Generation.Value} | metadata revision {Entry.Metadata?.Revision.Value ?? 0}";
}
