using System.Security.Cryptography;
using System.Text.Json;
using Kora.Application.Mcp;
using Kora.Core.Mcp;

namespace Kora.Application.Configuration;

public sealed partial class McpServerConfigurationService
{
    internal async Task<McpConnectionDescriptor> ResolveHttpsAsync(string serverId,
        McpAccountReference account, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(account);
        token.ThrowIfCancellationRequested();
        var selected = Load().SingleOrDefault(server => string.Equals(server.Id, serverId, StringComparison.Ordinal));
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
            if (definition.EnumerateObject().Any(property => property.Name is not ("url" or "type"))
                || !definition.TryGetProperty("url", out var url)
                || url.ValueKind != JsonValueKind.String
                || definition.TryGetProperty("type", out var type)
                    && (type.ValueKind != JsonValueKind.String
                        || !string.Equals(type.GetString(), "http", StringComparison.Ordinal)))
            { throw new InvalidDataException("The HTTPS MCP profile accepts only a URL and optional http type; inline credentials and launch configuration are forbidden."); }
            var endpoint = new McpEndpoint(new(url.GetString()!, UriKind.Absolute));
            if (endpoint != account.Destination)
            { throw new InvalidOperationException("The explicitly selected delegated account belongs to a different destination."); }
            token.ThrowIfCancellationRequested();
            if (!preferences.Load().IncludedMcpServerIds.Contains(serverId, StringComparer.Ordinal)
                || !SourceFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
            { throw new InvalidOperationException("The selected MCP source or inclusion changed during resolution."); }
            return new(serverId, selected.Name, path, Convert.ToHexStringLower(SHA256.HashData(bytes)), endpoint, account);
        }
        throw new InvalidOperationException("The selected MCP source is no longer configured.");
    }

    internal async Task ValidateCurrentAsync(McpConnectionDescriptor descriptor, CancellationToken token)
    {
        var current = await ResolveHttpsAsync(descriptor.ServerId, descriptor.Account, token).ConfigureAwait(false);
        if (!string.Equals(current.SourceRevision, descriptor.SourceRevision, StringComparison.Ordinal)
            || !string.Equals(current.SourcePath, descriptor.SourcePath, StringComparison.OrdinalIgnoreCase)
            || current.Endpoint != descriptor.Endpoint)
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
