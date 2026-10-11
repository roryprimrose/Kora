using System.Text;
using System.Text.Json;

using Kora.Core.Artifacts;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.Configuration;

public sealed class LocalArtifactDirectoryPreferences : IArtifactDirectoryPreferences
{
    private const string FileName = "artifact-directories.json";
    private const int MaximumPreferenceBytes = 64 * 1024;
    private readonly IPreferenceStore store;

    public LocalArtifactDirectoryPreferences(IApplicationDataPaths paths)
        : this(new LocalPreferenceStore(paths))
    {
    }

    internal LocalArtifactDirectoryPreferences(IPreferenceStore store) => this.store = store;

    public ArtifactDirectorySettings Load()
    {
        var text = store.ReadText(FileName, MaximumPreferenceBytes);
        if (text is null)
        {
            return ArtifactDirectorySettings.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            RequireProperties(root, "Version", "Skills", "Instructions", "Prompts", "Agents",
                "McpConfigurationFiles", "IncludedMcpServerIds");
            if (root.GetProperty("Version").GetInt32() != 2)
            {
                throw new InvalidDataException("The artifact directory preference version is unavailable.");
            }

            return new(
                ReadDirectories(root, "Skills"),
                ReadDirectories(root, "Instructions"),
                ReadDirectories(root, "Prompts"),
                ReadDirectories(root, "Agents"),
                ReadFiles(root, "McpConfigurationFiles"),
                ReadIdentifiers(root, "IncludedMcpServerIds"));
        }
        catch (Exception exception) when (exception is JsonException
            or InvalidOperationException
            or ArgumentException
            or NotSupportedException)
        {
            throw new InvalidDataException(
                "The saved artifact directory preferences are invalid. No additional artifact directories may be read.",
                exception);
        }
    }

    public void Save(ArtifactDirectorySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = new ArtifactDirectorySettings(
            Normalize(settings.SkillDirectories),
            Normalize(settings.InstructionDirectories),
            Normalize(settings.PromptDirectories),
            Normalize(settings.AgentDirectories),
            NormalizeFiles(settings.McpConfigurationFiles),
            settings.IncludedMcpServerIds);
        var text = JsonSerializer.Serialize(new
        {
            Version = 2,
            Skills = normalized.SkillDirectories,
            Instructions = normalized.InstructionDirectories,
            Prompts = normalized.PromptDirectories,
            Agents = normalized.AgentDirectories,
            McpConfigurationFiles = normalized.McpConfigurationFiles,
            IncludedMcpServerIds = normalized.IncludedMcpServerIds,
        });
        if (Encoding.UTF8.GetByteCount(text) > MaximumPreferenceBytes)
        {
            throw new InvalidDataException("The artifact directory preferences exceed the persisted byte limit.");
        }
        store.WriteText(FileName, text);
    }

    internal static string NormalizeDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Path.IsPathFullyQualified(directory))
        {
            throw new ArgumentException("Artifact directories must be absolute paths.", nameof(directory));
        }
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
    }

    internal static string NormalizeFile(string file)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        if (!Path.IsPathFullyQualified(file))
        {
            throw new ArgumentException("MCP configuration files must use absolute paths.", nameof(file));
        }
        return Path.GetFullPath(file);
    }

    private static string[] ReadDirectories(JsonElement root, string propertyName)
    {
        var value = root.GetProperty(propertyName);
        if (value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() > ArtifactDirectorySettings.MaximumPathsPerKind)
        {
            throw new InvalidDataException($"The {propertyName} artifact directories are not a bounded array.");
        }
        return Normalize(value.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String
                ? item.GetString()!
                : throw new InvalidDataException($"A {propertyName} artifact directory is not a string.")));
    }

    private static string[] ReadFiles(JsonElement root, string propertyName)
    {
        var value = root.GetProperty(propertyName);
        if (value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() > ArtifactDirectorySettings.MaximumPathsPerKind)
        {
            throw new InvalidDataException("The MCP configuration files are not a bounded array.");
        }
        return NormalizeFiles(value.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String
                ? item.GetString()!
                : throw new InvalidDataException("An MCP configuration file is not a string.")));
    }

    private static string[] ReadIdentifiers(JsonElement root, string propertyName)
    {
        var value = root.GetProperty(propertyName);
        if (value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() > ArtifactDirectorySettings.MaximumIncludedMcpServers)
        {
            throw new InvalidDataException("The included MCP server identifiers are not a bounded array.");
        }
        return value.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String
                ? item.GetString()!
                : throw new InvalidDataException("An included MCP server identifier is not a string."))
            .ToArray();
    }

    private static string[] Normalize(IEnumerable<string> directories)
    {
        var result = directories.Select(NormalizeDirectory).ToArray();
        if (result.Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Length)
        {
            throw new InvalidDataException("Artifact directories must be unique within each kind.");
        }
        return result;
    }

    private static string[] NormalizeFiles(IEnumerable<string> files)
    {
        var result = files.Select(NormalizeFile).ToArray();
        if (result.Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Length)
        {
            throw new InvalidDataException("MCP configuration files must be unique.");
        }
        return result;
    }

    private static void RequireProperties(JsonElement element, params string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new InvalidDataException("The preference contains missing, duplicate or unknown properties.");
        }
    }
}
