namespace Kora.Core.Agents;

public sealed record OwnedAgentProfileSnapshot(
    OwnedAgentProfileDestination Destination, string FileIdentity, OwnedAgentProfileDocument Document)
{
    public bool Matches(OwnedAgentProfileSnapshot? other) => other is not null
        && Destination == other.Destination
        && string.Equals(FileIdentity, other.FileIdentity, StringComparison.Ordinal)
        && Document.Document.CopyBytes().AsSpan().SequenceEqual(other.Document.Document.CopyBytes());
}
