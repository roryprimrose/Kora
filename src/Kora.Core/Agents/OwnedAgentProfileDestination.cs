namespace Kora.Core.Agents;

public sealed record OwnedAgentProfileDestination(string Name, string DisplayPath, string DirectoryIdentity)
{
    public string RootBindingIdentity { get; init; } = string.Empty;
}
