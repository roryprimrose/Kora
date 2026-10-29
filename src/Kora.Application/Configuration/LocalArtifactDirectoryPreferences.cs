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
    private readonly ArtifactDirectorySettings? defaults;

    public LocalArtifactDirectoryPreferences(IApplicationDataPaths paths)
        : this(new LocalPreferenceStore(paths))
    {
    }

    internal LocalArtifactDirectoryPreferences(IPreferenceStore store, ArtifactDirectorySettings? defaults = null)
    {
        this.store = store;
        this.defaults = defaults;
    }

    public ArtifactDirectorySettings Load()
    {
        var text = store.ReadText(FileName, MaximumPreferenceBytes);
        if (text is null)
        {
            return defaults ?? ArtifactDirectorySettings.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Version", out var versionElement))
            { throw new InvalidDataException("The artifact directory preference version is missing."); }
            var version = versionElement.GetInt32();
            if (version is not (2 or 3))
            {
                throw new InvalidDataException("The artifact directory preference version is unavailable.");
            }
            string[] properties = ["Version", "Skills", "Instructions", "Prompts", "Agents",
                "McpConfigurationFiles", "IncludedMcpServerIds"];
            RequireProperties(root, version == 2 ? properties : [.. properties, "UseDefaultDirectories"]);
            var useDefaults = version == 2 || root.GetProperty("UseDefaultDirectories").GetBoolean();
            var limit = useDefaults ? ArtifactDirectorySettings.MaximumPathsPerKind
                : ArtifactDirectorySettings.MaximumConfiguredDirectoriesPerKind;
            var settings = new ArtifactDirectorySettings(
                ReadDirectories(root, "Skills", limit),
                ReadDirectories(root, "Instructions", limit),
                ReadDirectories(root, "Prompts", limit),
                ReadDirectories(root, "Agents", limit),
                ReadFiles(root, "McpConfigurationFiles"),
                ReadIdentifiers(root, "IncludedMcpServerIds"), useDefaults);
            return defaults is null ? settings : settings.WithDefaults(defaults);
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
            settings.IncludedMcpServerIds, settings.UseDefaultDirectories);
        var text = JsonSerializer.Serialize(new
        {
            Version = 3,
            normalized.UseDefaultDirectories,
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

    private static string[] ReadDirectories(JsonElement root, string propertyName, int maximumPaths)
    {
        var value = root.GetProperty(propertyName);
        if (value.ValueKind != JsonValueKind.Array
            || value.GetArrayLength() > maximumPaths)
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
