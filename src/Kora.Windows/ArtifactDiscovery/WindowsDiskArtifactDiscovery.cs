using Kora.Core.Artifacts;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Windows.Artifacts;

public sealed class WindowsDiskArtifactDiscovery
{
    private const int MaximumFiles = 128;
    private readonly IReadOnlyList<ArtifactRoot> roots;
    private readonly WindowsOwnedInstructionStore? ownedInstructions;
    private readonly WindowsOwnedInstructionStore? ownedPrompts;

    public WindowsDiskArtifactDiscovery(
        IApplicationDataPaths paths,
        ArtifactDirectorySettings? additionalDirectories = null,
        WindowsOwnedInstructionStore? ownedInstructions = null,
        string? profileRoamingRoot = null,
        WindowsOwnedInstructionStore? ownedPrompts = null)
        : this(CreateRoots(
            paths,
            additionalDirectories ?? ArtifactDirectorySettings.Empty,
            profileRoamingRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)))
    {
        this.ownedInstructions = ownedInstructions ?? new WindowsOwnedInstructionStore(paths);
        this.ownedPrompts = ownedPrompts ?? new WindowsOwnedInstructionStore(paths, ArtifactKind.Prompt);
    }

    internal WindowsDiskArtifactDiscovery(
        IApplicationDataPaths paths,
        ArtifactDirectorySettings additionalDirectories,
        string profileRoamingRoot,
        WindowsOwnedInstructionStore? ownedInstructions = null)
        : this(CreateRoots(paths, additionalDirectories, profileRoamingRoot))
    {
        this.ownedInstructions = ownedInstructions;
    }

    internal WindowsDiskArtifactDiscovery(IReadOnlyList<ArtifactRoot> roots) =>
        this.roots = roots ?? throw new ArgumentNullException(nameof(roots));

    public IReadOnlyList<string> RootPaths =>
        roots.Select(root => root.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public IReadOnlyList<ArtifactWatchRoot> WatchRoots =>
        roots.GroupBy(root => root.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ArtifactWatchRoot(
                group.Key,
                group.Select(root => root.Pattern).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                group.Max(root => root.MaximumDepth),
                group.Any(root => root.Kind == ArtifactKind.Skill)))
            .ToArray();

    public IReadOnlyList<ArtifactDefinition> Load()
    {
        var files = new List<(ArtifactRoot Root, string Path)>();
        var managed = new List<ArtifactDefinition>();
        foreach (var root in roots)
        {
            var owned = root.Source is "kora-profile" ? root.Kind switch
            {
                ArtifactKind.Instruction => ownedInstructions,
                ArtifactKind.Prompt => ownedPrompts,
                _ => null,
            } : null;
            if (owned is not null)
            {
                foreach (var name in owned.ListVerified(CancellationToken.None))
                {
                    var snapshot = owned.ReadVerified(name, CancellationToken.None)
                        ?? throw new IOException("A discovered owned definition disappeared.");
                    managed.Add(snapshot.Document.Definition with { OwnedInstructionRevision = snapshot.Revision });
                }
                continue;
            }
            if (!Directory.Exists(root.Path)) { continue; }
            Discover(root, root.Path, 0, files);
        }
        if (files.Count > MaximumFiles)
        {
            throw new InvalidDataException($"Artifact discovery exceeds the {MaximumFiles}-file limit.");
        }
        return [.. managed, .. files.Select(item => Parse(item.Root, item.Path)).OfType<ArtifactDefinition>()];
    }

    private static ArtifactRoot[] CreateRoots(
        IApplicationDataPaths paths,
        ArtifactDirectorySettings additionalDirectories,
        string profileRoamingRoot)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(additionalDirectories);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileRoamingRoot);
        List<ArtifactRoot> discoveredRoots =
        [
            new(Path.Combine(paths.RoamingRoot, "Skills"), ArtifactKind.Skill, "kora-profile", "SKILL.md", 3),
            new(Path.Combine(paths.RoamingRoot, "Instructions"), ArtifactKind.Instruction, "kora-profile", "*.instructions.md", 0),
            new(Path.Combine(paths.RoamingRoot, "Prompts"), ArtifactKind.Prompt, "kora-profile", "*.prompt.md", 0),
            new(Path.Combine(paths.RoamingRoot, "Agents"), ArtifactKind.Agent, "kora-profile", "*.agent.md", 0),
            new(Path.Combine(profileRoamingRoot, "Code", "User", "prompts"), ArtifactKind.Prompt, "vscode-profile", "*.prompt.md", 0),
            new(Path.Combine(profileRoamingRoot, "Code", "User", "prompts"), ArtifactKind.Instruction, "vscode-profile", "*.instructions.md", 0),
            new(Path.Combine(profileRoamingRoot, "Code", "User", "prompts"), ArtifactKind.Agent, "vscode-profile", "*.agent.md", 0),
            new(Path.Combine(profileRoamingRoot, "Code", "User", "profiles"), ArtifactKind.Prompt, "vscode-profile", "*.prompt.md", 2),
            new(Path.Combine(profileRoamingRoot, "Code", "User", "profiles"), ArtifactKind.Instruction, "vscode-profile", "*.instructions.md", 2),
            new(Path.Combine(profileRoamingRoot, "Code", "User", "profiles"), ArtifactKind.Agent, "vscode-profile", "*.agent.md", 2),
            new(Path.Combine(profileRoamingRoot, "Code - Insiders", "User", "prompts"), ArtifactKind.Prompt, "vscode-insiders-profile", "*.prompt.md", 0),
            new(Path.Combine(profileRoamingRoot, "Code - Insiders", "User", "prompts"), ArtifactKind.Instruction, "vscode-insiders-profile", "*.instructions.md", 0),
            new(Path.Combine(profileRoamingRoot, "Code - Insiders", "User", "prompts"), ArtifactKind.Agent, "vscode-insiders-profile", "*.agent.md", 0),
            new(Path.Combine(profileRoamingRoot, "Code - Insiders", "User", "profiles"), ArtifactKind.Prompt, "vscode-insiders-profile", "*.prompt.md", 2),
            new(Path.Combine(profileRoamingRoot, "Code - Insiders", "User", "profiles"), ArtifactKind.Instruction, "vscode-insiders-profile", "*.instructions.md", 2),
            new(Path.Combine(profileRoamingRoot, "Code - Insiders", "User", "profiles"), ArtifactKind.Agent, "vscode-insiders-profile", "*.agent.md", 2),
        ];
        discoveredRoots.AddRange(CreateAdditionalRoots(additionalDirectories));
        return discoveredRoots
            .GroupBy(root => $"{root.Kind}\0{root.Path}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static IEnumerable<ArtifactRoot> CreateAdditionalRoots(ArtifactDirectorySettings settings)
    {
        foreach (var kind in Enum.GetValues<ArtifactKind>())
        {
            var pattern = kind switch
            {
                ArtifactKind.Skill => "SKILL.md",
                ArtifactKind.Instruction => "*.instructions.md",
                ArtifactKind.Prompt => "*.prompt.md",
                ArtifactKind.Agent => "*.agent.md",
                _ => throw new InvalidDataException("The artifact directory kind is invalid."),
            };
            var maximumDepth = kind == ArtifactKind.Skill ? 3 : 0;
            foreach (var directory in settings.GetDirectories(kind))
            {
                yield return new(directory, kind, "additional", pattern, maximumDepth);
            }
        }
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

    private ArtifactDefinition? Parse(ArtifactRoot root, string path)
    {
        if (root.Kind == ArtifactKind.Instruction && root.Source is "kora-profile" && ownedInstructions is not null)
        {
            var name = RemoveArtifactSuffix(Path.GetFileName(path), root.Kind);
            var snapshot = ownedInstructions.ReadVerified(name, CancellationToken.None)
                ?? throw new IOException("A discovered owned instruction disappeared.");
            return snapshot.Document.Definition with { OwnedInstructionRevision = snapshot.Revision };
        }
        var info = new FileInfo(path);
        if (info.Length is 0 or > ArtifactDefinition.MaximumContentLength)
        {
            throw new InvalidDataException($"Artifact file '{path}' is empty or exceeds the byte limit.");
        }
        var bytes = File.ReadAllBytes(path);
        var fileCommand = root.Kind == ArtifactKind.Skill
            ? new DirectoryInfo(Path.GetDirectoryName(path)!).Name
            : RemoveArtifactSuffix(Path.GetFileName(path), root.Kind);
        return ArtifactFileDocument.ParseInvocable(bytes, root.Kind, root.Source, fileCommand)?.Definition;
    }

    private static string RemoveArtifactSuffix(string fileName, ArtifactKind kind)
    {
        var suffix = kind switch
        {
            ArtifactKind.Prompt => ".prompt.md",
            ArtifactKind.Instruction => ".instructions.md",
            ArtifactKind.Agent => ".agent.md",
            _ => string.Empty,
        };
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

    public sealed record ArtifactWatchRoot(
        string Path,
        IReadOnlyList<string> Patterns,
        int MaximumDepth,
        bool WatchAllFiles);
}
