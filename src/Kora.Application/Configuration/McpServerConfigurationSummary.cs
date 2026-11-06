namespace Kora.Application.Configuration;

public sealed record McpServerConfigurationSummary(
    string Id,
    string Name,
    string Transport,
    string Source,
    bool IsIncluded,
    bool IsBuiltIn = false)
{
    public string Display => $"{Name} | {Transport} | {Source} | {(IsIncluded ? "included" : "excluded")}";
}
