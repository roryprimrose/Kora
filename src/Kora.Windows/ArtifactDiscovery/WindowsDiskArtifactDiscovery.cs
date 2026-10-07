using System.Security.Cryptography;
using System.Text;

using Kora.Core.Artifacts;
using Kora.Core.Dependencies;

namespace Kora.Windows.Artifacts;

public sealed class WindowsDiskArtifactDiscovery
{
    private const int MaximumFiles = 128;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly IReadOnlyList<ArtifactRoot> roots;

    public WindowsDiskArtifactDiscovery(IApplicationDataPaths paths)
        : this(CreateRoots(paths))
    {
    }

    internal WindowsDiskArtifactDiscovery(IReadOnlyList<ArtifactRoot> roots) =>
        this.roots = roots ?? throw new ArgumentNullException(nameof(roots));

    public IReadOnlyList<ArtifactDefinition> Load()
    {
        var files = new List<(ArtifactRoot Root, string Path)>();
        foreach (var root in roots.Where(root => Directory.Exists(root.Path)))
        {
            Discover(root, root.Path, 0, files);
        }
        if (files.Count > MaximumFiles)
        {
            throw new InvalidDataException($"Artifact discovery exceeds the {MaximumFiles}-file limit.");
        }
        return files.Select(item => Parse(item.Root, item.Path)).OfType<ArtifactDefinition>().ToArray();
    }

    private static IReadOnlyList<ArtifactRoot> CreateRoots(IApplicationDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return
        [
            new(Path.Combine(paths.RoamingRoot, "Skills"), ArtifactKind.Skill, "kora-profile", "SKILL.md", 3),
            new(Path.Combine(paths.RoamingRoot, "Instructions"), ArtifactKind.Instruction, "kora-profile", "*.instructions.md", 0),
            new(Path.Combine(paths.RoamingRoot, "Prompts"), ArtifactKind.Prompt, "kora-profile", "*.prompt.md", 0),
            new(Path.Combine(profile, ".copilot", "skills"), ArtifactKind.Skill, "copilot-profile", "SKILL.md", 1),
            new(Path.Combine(profile, ".agents", "skills"), ArtifactKind.Skill, "agents-profile", "SKILL.md", 1),
            new(Path.Combine(profile, ".claude", "skills"), ArtifactKind.Skill, "claude-profile", "SKILL.md", 1),
            new(Path.Combine(roaming, "Code", "User", "prompts"), ArtifactKind.Prompt, "vscode-profile", "*.prompt.md", 0),
            new(Path.Combine(roaming, "Code", "User", "prompts"), ArtifactKind.Instruction, "vscode-profile", "*.instructions.md", 0),
            new(Path.Combine(roaming, "Code", "User", "profiles"), ArtifactKind.Prompt, "vscode-profile", "*.prompt.md", 2),
            new(Path.Combine(roaming, "Code", "User", "profiles"), ArtifactKind.Instruction, "vscode-profile", "*.instructions.md", 2),
            new(Path.Combine(roaming, "Code - Insiders", "User", "prompts"), ArtifactKind.Prompt, "vscode-insiders-profile", "*.prompt.md", 0),
            new(Path.Combine(roaming, "Code - Insiders", "User", "prompts"), ArtifactKind.Instruction, "vscode-insiders-profile", "*.instructions.md", 0),
            new(Path.Combine(roaming, "Code - Insiders", "User", "profiles"), ArtifactKind.Prompt, "vscode-insiders-profile", "*.prompt.md", 2),
            new(Path.Combine(roaming, "Code - Insiders", "User", "profiles"), ArtifactKind.Instruction, "vscode-insiders-profile", "*.instructions.md", 2),
        ];
    }

    private static void Discover(
        ArtifactRoot root,
        string directory,
        int depth,
        List<(ArtifactRoot Root, string Path)> files)
    {
        if (IsReparsePoint(directory))
        {
            return;
        }
        files.AddRange(Directory.EnumerateFiles(directory, root.Pattern, SearchOption.TopDirectoryOnly)
            .Where(path => !IsReparsePoint(path))
            .Select(path => (root, path)));
        if (depth >= root.MaximumDepth)
        {
            return;
        }
        foreach (var child in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
        {
            Discover(root, child, depth + 1, files);
            if (files.Count > MaximumFiles)
            {
                return;
            }
        }
    }

    private static ArtifactDefinition? Parse(ArtifactRoot root, string path)
    {
        var info = new FileInfo(path);
        if (info.Length is 0 or > ArtifactDefinition.MaximumContentLength)
        {
            throw new InvalidDataException($"Artifact file '{path}' is empty or exceeds the byte limit.");
        }
        var bytes = File.ReadAllBytes(path);
        string text;
        try
        {
            text = Utf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException($"Artifact file '{path}' is not strict UTF-8.", exception);
        }
        var metadata = ParseFrontmatter(text, path, out var body);
        if (root.Kind == ArtifactKind.Skill
            && metadata.TryGetValue("user-invocable", out var userInvocable)
            && string.Equals(userInvocable, "false", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var fileCommand = root.Kind == ArtifactKind.Skill
            ? new DirectoryInfo(Path.GetDirectoryName(path)!).Name
            : RemoveArtifactSuffix(Path.GetFileName(path), root.Kind);
        var command = Slug(metadata.GetValueOrDefault("name") ?? fileCommand);
        var name = metadata.GetValueOrDefault("name") ?? command;
        var description = metadata.GetValueOrDefault("description")
            ?? (root.Kind == ArtifactKind.Prompt
                ? $"Run the {name} prompt."
                : throw new InvalidDataException($"Artifact file '{path}' requires a description."));
        if (root.Kind == ArtifactKind.Skill
            && !string.Equals(command, fileCommand, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Skill '{path}' must use a name matching its folder.");
        }
        var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var spokenNames = string.Equals(name, command, StringComparison.OrdinalIgnoreCase)
            ? [name]
            : new[] { name, command };
        return new(
            $"disk.{root.Source}.{root.Kind.ToString().ToLowerInvariant()}.{command}",
            root.Kind,
            name,
            description,
            command,
            spokenNames,
            root.Source,
            $"disk-{digest[..12]}",
            digest,
            body);
    }

    private static Dictionary<string, string> ParseFrontmatter(string text, string path, out string body)
    {
        using var reader = new StringReader(text);
        if (!string.Equals(reader.ReadLine(), "---", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Artifact file '{path}' requires YAML frontmatter.");
        }
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        var lines = new List<string>();
        string? line;
        var closed = false;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.Equals(line, "---", StringComparison.Ordinal))
            {
                closed = true;
                break;
            }
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                throw new InvalidDataException($"Artifact file '{path}' contains unsupported frontmatter.");
            }
            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (!metadata.TryAdd(key, Unquote(value)))
            {
                throw new InvalidDataException($"Artifact file '{path}' contains duplicate frontmatter.");
            }
        }
        if (!closed)
        {
            throw new InvalidDataException($"Artifact file '{path}' has unterminated frontmatter.");
        }
        while ((line = reader.ReadLine()) is not null)
        {
            lines.Add(line);
        }
        body = string.Join(Environment.NewLine, lines).Trim();
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new InvalidDataException($"Artifact file '{path}' requires instruction content.");
        }
        return metadata;
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == value[^1] && value[0] is '\'' or '"'
            ? value[1..^1]
            : value;

    private static string Slug(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(character);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }
        var result = builder.ToString().Trim('-');
        if (string.IsNullOrWhiteSpace(result) || result.Length > 64)
        {
            throw new InvalidDataException("Artifact names must produce a bounded lowercase slash command.");
        }
        return result;
    }

    private static string RemoveArtifactSuffix(string fileName, ArtifactKind kind)
    {
        var suffix = kind == ArtifactKind.Prompt ? ".prompt.md" : ".instructions.md";
        return fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^suffix.Length]
            : fileName;
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;

    internal sealed record ArtifactRoot(
        string Path,
        ArtifactKind Kind,
        string Source,
        string Pattern,
        int MaximumDepth);
}
