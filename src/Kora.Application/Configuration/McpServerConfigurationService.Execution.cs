using System.Security.Cryptography;
using System.Text.Json;
using Kora.Application.Mcp;
using Kora.Core.Mcp;
using Kora.Core.Customizations;

namespace Kora.Application.Configuration;

public sealed partial class McpServerConfigurationService
{
    internal Task<McpConnectionDescriptor> ResolveHttpsAsync(string serverId,
        McpAccountReference account, CancellationToken token) => ResolveAsync(serverId, new(account), token);

    internal async Task<McpConnectionDescriptor> ResolveAsync(string serverId,
        McpConnectionIdentity? identity, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var useRevision = UseControlRevision;
        McpServerConfigurationSummary? selected;
        long sourceControlRevision;
        lock (sourceGate)
        {
            selected = Load().SingleOrDefault(server => string.Equals(server.Id, serverId, StringComparison.Ordinal));
            sourceControlRevision = SourceControlRevision(serverId);
        }
        if (selected is not { IsIncluded: true })
        { throw new InvalidOperationException("The exact configured MCP server is not included."); }
        foreach (var path in SourceFiles)
        {
            if (!string.Equals(CreateId(path, selected.Name), serverId, StringComparison.Ordinal)) { continue; }
            var bytes = await ReadBoundedAsync(path, token).ConfigureAwait(false);
            var summaries = new List<McpServerConfigurationSummary>();
            LoadBytes(bytes, path, [serverId], summaries);
            if (!summaries.Any(server => string.Equals(server.Id, serverId, StringComparison.Ordinal)))
            { throw new InvalidOperationException("The selected MCP source changed during resolution."); }
            using var document = JsonDocument.Parse(Utf8.GetString(bytes), new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = 16,
            });
            var root = document.RootElement;
            var servers = root.TryGetProperty("servers", out var value) ? value : root.GetProperty("mcpServers");
            var definition = servers.GetProperty(selected.Name);
            var raw = System.Text.Encoding.UTF8.GetBytes(definition.GetRawText());
            McpConnectionIdentity resolved;
            if (definition.TryGetProperty("command", out _))
            {
                resolved = new(McpDefinitionDocument.ValidateStdioServer(raw));
                if (identity is not null && identity != resolved)
                { throw new InvalidOperationException("The exact local MCP profile differs from the selected identity."); }
            }
            else
            {
                var endpoint = McpDefinitionDocument.ValidateHttpsServer(raw);
                if (identity?.Account is not { } account || endpoint != account.Destination)
                { throw new InvalidOperationException("Select the exact delegated account for this HTTPS destination."); }
                resolved = identity;
            }
            token.ThrowIfCancellationRequested();
            if (!preferences.Load().IncludedMcpServerIds.Contains(serverId, StringComparer.Ordinal)
                || !SourceFiles.Contains(path, StringComparer.OrdinalIgnoreCase)
                || usePolicy?.IsDisabled(Kora.Core.Artifacts.ArtifactUseIdentity.Mcp(serverId)) == true
                || UseControlRevision != useRevision
                || SourceControlRevision(serverId) != sourceControlRevision)
            { throw new InvalidOperationException("The selected MCP source or inclusion changed during resolution."); }
            var scope = string.Equals(Path.GetFullPath(path),
                Path.GetFullPath(Path.Combine(paths.RoamingRoot, "Mcp", "mcp.json")), StringComparison.OrdinalIgnoreCase)
                ? CustomizationScope.Kora : CustomizationScope.User;
            return new(serverId, selected.Name, path, Convert.ToHexStringLower(SHA256.HashData(bytes)),
                resolved, useRevision, scope, sourceControlRevision);
        }
        throw new InvalidOperationException("The selected MCP source is no longer configured.");
    }

    internal async Task ValidateCurrentAsync(McpConnectionDescriptor descriptor, CancellationToken token)
    {
        var current = await ResolveAsync(descriptor.ServerId, descriptor.Identity, token).ConfigureAwait(false);
        if (!string.Equals(current.SourceRevision, descriptor.SourceRevision, StringComparison.Ordinal)
            || !string.Equals(current.SourcePath, descriptor.SourcePath, StringComparison.OrdinalIgnoreCase)
            || current.UseControlRevision != descriptor.UseControlRevision
            || current.SourceControlRevision != descriptor.SourceControlRevision
            || current.Identity != descriptor.Identity)
        { throw new InvalidOperationException("The exact MCP source revision changed; review and discovery must be repeated."); }
    }

    private static async Task<byte[]> ReadBoundedAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[MaximumConfigurationBytes + 1];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(count), token).ConfigureAwait(false);
            if (read == 0) { break; }
            count += read;
        }
        if (count is 0 or > MaximumConfigurationBytes)
        { throw new InvalidDataException("MCP configuration is empty or exceeds its byte limit."); }
        return buffer.AsSpan(0, count).ToArray();
    }
}
