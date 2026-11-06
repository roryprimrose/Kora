using Kora.Core.Mcp;

namespace Kora;

/// <summary>Private displayed metadata only. Selection and effective state are host projections, not grants.</summary>
internal sealed record McpCapabilityRow(string ServerId, string ServerName, string SourceLabel, string ScopeLabel,
    McpCapability Capability, bool IsSelected, bool IsEffectiveConsideration, string? AvailabilityReason = null)
{
    internal string Label => Capability.Name + (IsSelected ? string.Empty : " (Disabled)")
        + (IsSelected && !IsEffectiveConsideration ? " (Not currently available)" : string.Empty);
    internal string Identity => ServerId + "\0" + Capability.Kind + "\0" + Capability.Id;
    internal bool Matches(string? query) => CustomizationMetadataMatch.Matches(query,
        Label + " " + ServerName, Capability.Id + " " + SourceLabel, ScopeLabel);
    public override string ToString() => nameof(McpCapabilityRow);
}