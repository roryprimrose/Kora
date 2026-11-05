using Kora.Core.Dependencies;
using Kora.Core.Tools;

namespace Kora.Core.Mcp;

/// <summary>Host-owned identities, never identities supplied by a discovered server.</summary>
public static class KoraMcpCatalog
{
    public const string ServerId = "builtin.kora";

    public static IReadOnlyList<CapabilityDescriptor> Descriptors { get; } = Array.AsReadOnly(
        ReadOnlyCapabilityCatalog.Descriptors
            .Concat(SettingsDiscoveryTools.Descriptors)
            .Concat(CustomizationDiscoveryTools.Descriptors)
            .Concat(SessionDiscoveryTool.Descriptors)
            .Concat(Network.LocalWebPageTool.Descriptors).ToArray());

    public static IReadOnlyList<string> ToolIds { get; } = Array.AsReadOnly(
        Descriptors.Select(tool => tool.Id).Append(ModelToolProtocol.AskUserToolName).ToArray());

}
