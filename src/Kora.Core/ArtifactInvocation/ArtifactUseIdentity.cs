using System.Security.Cryptography;
using System.Text;

namespace Kora.Core.Artifacts;

public static class ArtifactUseIdentity
{
    public static string File(string canonicalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        if (!Path.IsPathFullyQualified(canonicalPath))
        { throw new ArgumentException("Use control requires a host-resolved absolute source.", nameof(canonicalPath)); }
        return "file." + Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes(Path.GetFullPath(canonicalPath).ToUpperInvariant())));
    }

    public static string Mcp(string serverId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverId);
        return ArtifactDefinition.ValidateId("mcp." + serverId);
    }

    public static string McpServerId(string canonicalPath, string name) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{Path.GetFullPath(canonicalPath).ToUpperInvariant()}\0{name}")));
}
