using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Artifacts;

namespace Kora.Application.Configuration;

public sealed partial class McpServerConfigurationService
{
    private const int MaximumConfigurationBytes = 256 * 1024;
    private const int MaximumServers = 128;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly IApplicationDataPaths paths;
    private readonly IArtifactDirectoryPreferences preferences;
    private readonly string profileRoamingRoot;
    private readonly IArtifactUsePolicy? usePolicy;

    public event EventHandler? Changed;
    internal long UseControlRevision => usePolicy?.Revision ?? 0;

    public McpServerConfigurationService(
        IApplicationDataPaths paths,
        IArtifactDirectoryPreferences preferences, IArtifactUsePolicy? usePolicy = null)
        : this(paths, preferences, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), usePolicy)
    {
    }

    internal McpServerConfigurationService(
        IApplicationDataPaths paths,
        IArtifactDirectoryPreferences preferences,
        string profileRoamingRoot, IArtifactUsePolicy? usePolicy = null)
    {
        this.paths = paths;
        this.preferences = preferences;
        this.profileRoamingRoot = profileRoamingRoot;
        this.usePolicy = usePolicy;
    }

    public IReadOnlyList<McpServerConfigurationSummary> Load()
    {
        var settings = preferences.Load();
        var sources = DefaultFiles()
            .Select(path => new Source(path, Required: false))
            .Concat(settings.McpConfigurationFiles.Select(path => new Source(path, Required: true)))
            .GroupBy(source => source.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(source => source.Required).First());
        var servers = new List<McpServerConfigurationSummary>();
        foreach (var source in sources)
        {
            if (!File.Exists(source.Path))
            {
                if (source.Required)
                {
                    throw new FileNotFoundException(
                        "A configured MCP server configuration file is unavailable.",
                        source.Path);
                }
                continue;
            }
            LoadFile(source.Path, settings.IncludedMcpServerIds, servers);
            if (servers.Count > MaximumServers)
            {
                throw new InvalidDataException($"MCP configuration exceeds the {MaximumServers}-server limit.");
            }
        }
        return servers
            .Select(server => usePolicy?.IsDisabled(ArtifactUseIdentity.Mcp(server.Id)) == true
                ? server with { IsIncluded = false } : server)
            .OrderBy(server => server.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(server => server.Source, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<string> SourceFiles
    {
        get
        {
            var settings = preferences.Load();
            return DefaultFiles()
                .Concat(settings.McpConfigurationFiles)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    internal void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private IEnumerable<string> DefaultFiles()
    {
        yield return Path.Combine(paths.RoamingRoot, "Mcp", "mcp.json");
        yield return Path.Combine(profileRoamingRoot, "Code", "User", "mcp.json");
        yield return Path.Combine(profileRoamingRoot, "Code - Insiders", "User", "mcp.json");
        yield return Path.Combine(profileRoamingRoot, "Claude", "claude_desktop_config.json");
    }

    private static void LoadFile(
        string path,
        IReadOnlyList<string> includedServerIds,
        List<McpServerConfigurationSummary> servers)
    {
        var info = new FileInfo(path);
        if (info.Length is 0 or > MaximumConfigurationBytes)
        {
            throw new InvalidDataException($"MCP configuration file '{path}' is empty or exceeds its byte limit.");
        }
        LoadBytes(File.ReadAllBytes(path), path, includedServerIds, servers);
    }

    internal static void LoadBytes(byte[] bytes, string path, IReadOnlyList<string> includedServerIds,
        List<McpServerConfigurationSummary> servers)
    {
        if (bytes.Length is 0 or > MaximumConfigurationBytes)
        { throw new InvalidDataException("MCP configuration is empty or exceeds its byte limit."); }
        string text;
        try
        {
            text = Utf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException($"MCP configuration file '{path}' is not strict UTF-8.", exception);
        }
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = 16,
            });
            var root = document.RootElement;
            ValidateUniqueNames(root);
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"MCP configuration file '{path}' must contain a JSON object.");
            }
            var hasServers = root.TryGetProperty("servers", out var vscodeServers);
            var hasMcpServers = root.TryGetProperty("mcpServers", out var desktopServers);
            if (hasServers == hasMcpServers)
            {
                throw new InvalidDataException(
                    $"MCP configuration file '{path}' must contain exactly one servers or mcpServers object.");
            }
            var configuredServers = hasServers ? vscodeServers : desktopServers;
            if (configuredServers.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"MCP configuration file '{path}' has an invalid server collection.");
            }
            foreach (var server in configuredServers.EnumerateObject())
            {
                if (server.Value.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException($"MCP server '{server.Name}' in '{path}' must be an object.");
                }
                var hasCommand = server.Value.TryGetProperty("command", out var command)
                    && command.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(command.GetString());
                var hasUrl = server.Value.TryGetProperty("url", out var url)
                    && url.ValueKind == JsonValueKind.String
                    && Uri.TryCreate(url.GetString(), UriKind.Absolute, out var parsed)
                    && parsed.Scheme is "http" or "https";
                if (hasCommand == hasUrl)
                {
                    throw new InvalidDataException(
                        $"MCP server '{server.Name}' in '{path}' must declare exactly one command or HTTP(S) URL.");
                }
                if (string.IsNullOrWhiteSpace(server.Name) || server.Name.Length > 128)
                {
                    throw new InvalidDataException($"MCP configuration file '{path}' contains an invalid server name.");
                }
                var id = CreateId(path, server.Name);
                servers.Add(new(
                    id,
                    server.Name,
                    hasCommand ? "stdio" : "http",
                    Path.GetFileName(path),
                    includedServerIds.Contains(id, StringComparer.Ordinal)));
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"MCP configuration file '{path}' is invalid JSON.", exception);
        }
    }

    public static string CreateId(string path, string name) =>
        ArtifactUseIdentity.McpServerId(path, name);

    private static void ValidateUniqueNames(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                { throw new InvalidDataException("Duplicate MCP configuration fields are not admitted."); }
                ValidateUniqueNames(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) { ValidateUniqueNames(item); }
        }
    }

    private sealed record Source(string Path, bool Required);
}
