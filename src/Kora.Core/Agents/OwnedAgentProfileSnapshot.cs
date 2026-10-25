namespace Kora.Core.Agents;

public sealed record OwnedAgentProfileSnapshot(
    OwnedAgentProfileDestination Destination, string FileIdentity, OwnedAgentProfileDocument Document);
