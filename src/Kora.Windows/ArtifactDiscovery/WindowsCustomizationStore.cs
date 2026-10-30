using System.Security.Cryptography;
using System.Text;

using Kora.Core.Artifacts;
using Kora.Core.Configuration;
using Kora.Core.Customizations;
using Kora.Core.Dependencies;

namespace Kora.Windows.Artifacts;

public sealed partial class WindowsCustomizationStore : ICustomizationStore, ICustomizationMetadataStore
{
    private static readonly string[] EditorNames = ["Code", "Code - Insiders"];
    private readonly IApplicationDataPaths paths;
    private readonly string profileRoaming;
    private readonly string profile;
    private readonly IArtifactDirectoryPreferences? directoryPreferences;
    private readonly Lock gate = new();
    private readonly Dictionary<string, Target> targets = new(StringComparer.Ordinal);
    internal Action<string>? AfterComponentPublished { get; set; }
    private sealed record Root(string Path, CustomizationKind Kind, CustomizationScope Scope);
    private sealed record Target(CustomizationSource Source, string Directory, string FileName, string? Entry = null)
    {
        internal string? McpCreationRevision { get; init; }
    }

    public WindowsCustomizationStore(IApplicationDataPaths paths, IArtifactDirectoryPreferences? directoryPreferences = null)
        : this(paths, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), directoryPreferences)
    { }

    internal WindowsCustomizationStore(IApplicationDataPaths paths, string profileRoaming, string profile,
        IArtifactDirectoryPreferences? directoryPreferences = null)
    {
        this.paths = paths;
        this.profileRoaming = profileRoaming;
        this.profile = profile;
        this.directoryPreferences = directoryPreferences;
    }

    public Task<IReadOnlyList<CustomizationSource>> List(CancellationToken token) => ListCore(false, token);

    public Task<IReadOnlyList<CustomizationSource>> ListMetadataSources(CancellationToken token) => ListCore(true, token);

    private Task<IReadOnlyList<CustomizationSource>> ListCore(bool metadataOnly, CancellationToken token) => Task.Run(() =>
    {
        lock (gate)
        {
            var found = new List<CustomizationSource>();
            foreach (var root in Roots())
            {
                token.ThrowIfCancellationRequested();
                if (metadataOnly && !Kora.Core.Tools.CustomizationDiscoveryTools.IsSupported(root.Kind)) { continue; }
                if (!Directory.Exists(root.Path)) { continue; }
                using var pinned = WindowsOwnedDefinitionFileAccess.PinDefinitionDirectory(root.Path, false, token);
                if (root.Kind == CustomizationKind.Skill)
                {
                    var directories = Directory.EnumerateDirectories(root.Path).Take(129).ToArray();
                    if (directories.Length > 128) { throw new InvalidDataException("A skill source exceeds 128 packages."); }
                    foreach (var directory in directories)
                    {
                        var file = Path.Combine(directory, "SKILL.md");
                        var target = MakeTarget(root, file, new DirectoryInfo(directory).Name);
                        if (File.Exists(file) || File.Exists(JournalPath(target)))
                        { Add(found, target); }
                    }
                }
                else if (root.Kind == CustomizationKind.McpServer)
                {
                    var file = Path.Combine(root.Path, "mcp.json");
                    var bytes = WindowsOwnedDefinitionFileAccess.ReadDefinitionFile(file, 256 * 1024, token);
                    if (bytes is null) { continue; }
                    foreach (var name in new McpDefinitionDocument(bytes.Bytes).Names)
                    { Add(found, MakeTarget(root, file, name)); }
                }
                else
                {
                    var suffix = Suffix(root.Kind);
                    var files = Directory.EnumerateFiles(root.Path, "*" + suffix).Take(129).ToArray();
                    if (files.Length > 128) { throw new InvalidDataException("A definition source exceeds 128 files."); }
                    foreach (var file in files)
                    { Add(found, MakeTarget(root, file, Path.GetFileName(file)[..^suffix.Length])); }
                }
                if (found.Count > 512) { throw new InvalidDataException("Customization inventory exceeds 512 entries."); }
            }
            foreach (var recovery in metadataOnly ? [] : ListJournalTargets(token))
            {
                if (!found.Any(source => string.Equals(source.Id, recovery.Source.Id, StringComparison.Ordinal)))
                { Add(found, recovery); }
            }
            return (IReadOnlyList<CustomizationSource>)found.OrderBy(source => source.Scope)
                .ThenBy(source => source.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }, token);

    private void Add(List<CustomizationSource> found, Target target)
    {
        if (found.Any(source => string.Equals(source.Id, target.Source.Id, StringComparison.Ordinal))) { return; }
        targets[target.Source.Id] = target;
        found.Add(target.Source);
    }

    public Task<CustomizationSource> Create(CustomizationKind kind, CustomizationScope scope, string name, CancellationToken token) =>
        Task.Run(() =>
        {
            lock (gate)
            {
                OwnedInstructionNames.WriteName(name);
                if (kind == CustomizationKind.McpServer && scope != CustomizationScope.Kora)
                { throw new InvalidDataException("MCP editing is limited to Kora's canonical roaming Mcp\\mcp.json. Personal and additional files remain inspection-only."); }
                if (scope is not (CustomizationScope.Kora or CustomizationScope.User))
                { throw new InvalidDataException("Built-in definitions cannot be written."); }
                if (scope == CustomizationScope.User && kind == CustomizationKind.Instruction)
                { throw new InvalidOperationException("This increment edits one existing registered original only. Create a Kora instruction or select an existing personal original instead."); }
                var root = Roots().FirstOrDefault(root => root.Kind == kind && root.Scope == scope)
                    ?? throw new InvalidOperationException("No directory is configured for this content kind and scope. Add a location in Settings before creating it.");
                var file = kind == CustomizationKind.Skill ? Path.Combine(root.Path, name, "SKILL.md")
                    : Path.Combine(root.Path, kind == CustomizationKind.McpServer ? "mcp.json" : name + Suffix(kind));
                var target = MakeTarget(root, file, name);
                if (kind == CustomizationKind.McpServer)
                {
                    var captured = CaptureMcp(target, token);
                    var document = captured.State.Files.Count == 0 ? null : new McpDefinitionDocument(captured.State.Files.Single().Bytes);
                    document?.RequireManagedShape();
                    if (document?.Names.Contains(name, StringComparer.Ordinal) == true)
                    { throw new InvalidOperationException("That exact MCP entry already exists. Select and review it instead."); }
                    target = target with { McpCreationRevision = captured.Revision };
                }
                if (File.Exists(file) && (kind != CustomizationKind.McpServer
                    || new McpDefinitionDocument(WindowsOwnedDefinitionFileAccess.ReadDefinitionFile(file, 256 * 1024, token)!.Bytes)
                        .Names.Contains(name, StringComparer.Ordinal)))
                { throw new InvalidOperationException("That exact destination already exists. Select it or choose another name."); }
                if (File.Exists(JournalPath(target)))
                { throw new InvalidOperationException("That destination has an unfinished transaction. Recover it first."); }
                targets[target.Source.Id] = target;
                return target.Source;
            }
        }, token);

    public Task<CustomizationSnapshot> Read(CustomizationSource source, CancellationToken token) => Task.Run(() =>
    {
        lock (gate)
        {
            var target = Resolve(source);
            if (source.Kind == CustomizationKind.McpServer)
            {
                RequireOwnedMcp(target);
                var captured = CaptureMcp(target, token);
                return ExposeMcp(target, captured);
            }
            if (IsInstructionOriginal(source)) { RequireInstructionRegistration(target, token); }
            if (File.Exists(JournalPath(target)))
            { throw new InvalidOperationException("This source has an interrupted transaction. Review recovery before editing or enabling it."); }
            var snapshot = Expose(target, Capture(target, token));
            if (IsInstructionOriginal(source))
            {
                WindowsOwnedDefinitionFileAccess.InspectInstructionOriginal(source.Location, token);
                var document = ArtifactFileDocument.Parse(snapshot.Files.Single().CopyBytes(), ArtifactKind.Instruction,
                    "profile-instruction", source.Name);
                document.RequireProfileInstructionSource();
                RequireInstructionRegistration(target, token);
            }
            return snapshot;
        }
    }, token);

    private Target Resolve(CustomizationSource source)
    {
        if (!targets.TryGetValue(source.Id, out var target) || target.Source != source || source.IsReadOnly)
        { throw new InvalidOperationException("Select one exact current writable source from this host's inventory."); }
        var directory = source.Kind == CustomizationKind.Skill ? Path.GetDirectoryName(target.Directory) : target.Directory;
        if (!Roots().Any(root => root.Kind == source.Kind && root.Scope == source.Scope
            && string.Equals(root.Path, directory, StringComparison.OrdinalIgnoreCase)))
        { throw new InvalidOperationException("This source directory is no longer configured. Choose a current source."); }
        return target;
    }

    private static string Suffix(CustomizationKind kind) => kind switch
    {
        CustomizationKind.Instruction => ".instructions.md",
        CustomizationKind.Agent => ".agent.md",
        CustomizationKind.Prompt => ".prompt.md",
        _ => throw new InvalidDataException("This kind has no standalone Markdown filename."),
    };

    private static Target MakeTarget(Root root, string file, string name)
    {
        var id = root.Kind == CustomizationKind.McpServer
            ? ArtifactUseIdentity.Mcp(ArtifactUseIdentity.McpServerId(file, name)) : ArtifactUseIdentity.File(file);
        var primary = root.Kind == CustomizationKind.Skill ? "SKILL.md"
            : root.Kind == CustomizationKind.McpServer ? "server.json" : Path.GetFileName(file);
        return new(new(id, root.Kind, root.Scope, name, file, primary),
            Path.GetDirectoryName(file)!, Path.GetFileName(file), root.Kind == CustomizationKind.McpServer ? name : null);
    }

    private IEnumerable<Root> Roots()
    {
        if (directoryPreferences is null) { return DefaultRoots(); }
        var settings = directoryPreferences.Load().WithDefaults(
            ArtifactDirectorySettings.DefaultDirectories(paths.RoamingRoot, profileRoaming, profile));
        return ConfiguredRoots(settings);
    }

    private IEnumerable<Root> ConfiguredRoots(ArtifactDirectorySettings settings)
    {
        foreach (var kind in new[] { CustomizationKind.Skill, CustomizationKind.Instruction,
            CustomizationKind.Agent, CustomizationKind.Prompt })
        {
            var koraPath = Path.Combine(paths.RoamingRoot, kind == CustomizationKind.Agent ? "Agents" : kind + "s");
            foreach (var path in settings.GetDirectories(CustomizationLayout.ArtifactKind(kind)))
            {
                if (kind != CustomizationKind.Skill && EditorNames.Any(editor =>
                    string.Equals(path, Path.Combine(profileRoaming, editor, "User", "profiles"), StringComparison.OrdinalIgnoreCase)))
                {
                    foreach (var directory in ProfileDirectories(path))
                    { yield return new(Path.Combine(directory, "prompts"), kind, CustomizationScope.User); }
                }
                else
                {
                    yield return new(path, kind, string.Equals(path, koraPath, StringComparison.OrdinalIgnoreCase)
                        ? CustomizationScope.Kora : CustomizationScope.User);
                }
            }
        }
        foreach (var root in DefaultRoots().Where(root => root.Kind == CustomizationKind.McpServer))
        { yield return root; }
    }

    private IEnumerable<Root> DefaultRoots()
    {
        foreach (var scope in new[] { CustomizationScope.Kora, CustomizationScope.User })
        {
            yield return new(scope == CustomizationScope.Kora ? Path.Combine(paths.RoamingRoot, "Skills")
                : Path.Combine(profile, ".copilot", "skills"), CustomizationKind.Skill, scope);
            if (scope == CustomizationScope.User)
            { yield return new(Path.Combine(profile, ".agents", "skills"), CustomizationKind.Skill, scope); }
            foreach (var kind in new[] { CustomizationKind.Instruction, CustomizationKind.Agent, CustomizationKind.Prompt })
            {
                if (scope == CustomizationScope.Kora)
                { yield return new(Path.Combine(paths.RoamingRoot, kind == CustomizationKind.Agent ? "Agents" : kind + "s"), kind, scope); }
                else
                {
                    foreach (var root in EditorRoots())
                    { yield return new(Path.Combine(root, "prompts"), kind, scope); }
                }
            }
            if (scope == CustomizationScope.Kora)
            { yield return new(Path.Combine(paths.RoamingRoot, "Mcp"), CustomizationKind.McpServer, scope); }
            else
            {
                foreach (var root in EditorRoots()) { yield return new(root, CustomizationKind.McpServer, scope); }
            }
        }
    }

    private IEnumerable<string> EditorRoots()
    {
        foreach (var editor in EditorNames)
        {
            var root = Path.Combine(profileRoaming, editor, "User");
            yield return root;
            var profiles = Path.Combine(root, "profiles");
            foreach (var directory in ProfileDirectories(profiles)) { yield return directory; }
        }
    }

    private static IEnumerable<string> ProfileDirectories(string profiles)
    {
        if (!Directory.Exists(profiles)) { yield break; }
        using var pinned = WindowsOwnedDefinitionFileAccess.PinDefinitionDirectory(profiles, false, CancellationToken.None);
        var directories = Directory.EnumerateDirectories(profiles).Take(17).ToArray();
        if (directories.Length > 16) { throw new InvalidDataException("The editor profile inventory exceeds 16 profiles."); }
        foreach (var directory in directories) { yield return directory; }
    }

    private sealed record DiskFile(string Name, byte[] Bytes, string Identity);
    private sealed record DiskState(string DirectoryIdentity, IReadOnlyList<DiskFile> Files)
    {
        internal string Revision
        {
            get
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                hash.AppendData(Encoding.UTF8.GetBytes(DirectoryIdentity + "\0"));
                foreach (var file in Files.OrderBy(file => file.Name, StringComparer.Ordinal))
                {
                    hash.AppendData(Encoding.UTF8.GetBytes(file.Name + "\0" + file.Identity + "\0"));
                    hash.AppendData(SHA256.HashData(file.Bytes));
                }
                return Convert.ToHexStringLower(hash.GetHashAndReset());
            }
        }
    }

    private static DiskState Capture(Target target, CancellationToken token)
    {
        using var pinned = WindowsOwnedDefinitionFileAccess.PinDefinitionDirectory(target.Directory, false, token);
        var files = new List<DiskFile>();
        if (target.Source.Kind == CustomizationKind.Skill)
        {
            var entries = 0;
            CaptureDirectory(target.Directory, target.Directory, files, ref entries, token);
        }
        else
        {
            var file = WindowsOwnedDefinitionFileAccess.ReadDefinitionFile(Path.Combine(target.Directory, target.FileName),
                target.Entry is null ? 64 * 1024 : 256 * 1024, token);
            if (file is not null) { files.Add(new(target.FileName, file.Bytes, file.Identity)); }
        }
        return new(pinned.Identity, files);
    }

    private static void CaptureDirectory(string root, string directory, List<DiskFile> files, ref int entries, CancellationToken token)
    {
        using var pinned = WindowsOwnedDefinitionFileAccess.PinDefinitionDirectory(directory, false, token);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            token.ThrowIfCancellationRequested();
            if (++entries > 128) { throw new InvalidDataException("The complete package exceeds the editor's directory-entry limit."); }
            var name = CustomizationFile.ValidateName(Path.GetRelativePath(root, entry));
            if (File.GetAttributes(entry).HasFlag(FileAttributes.Directory))
            { CaptureDirectory(root, entry, files, ref entries, token); }
            else
            {
                var captured = WindowsOwnedDefinitionFileAccess.ReadDefinitionFile(entry, 64 * 1024, token)
                    ?? throw new IOException("A component disappeared during capture.");
                _ = new CustomizationFile(name, captured.Bytes);
                files.Add(new(name, captured.Bytes, captured.Identity));
                if (files.Count > CustomizationSnapshot.MaximumFiles || files.Sum(file => file.Bytes.Length) > CustomizationSnapshot.MaximumTotalBytes)
                { throw new InvalidDataException("The complete package exceeds the editor's bounded file/byte limits."); }
            }
        }
    }

    private static CustomizationSnapshot Expose(Target target, DiskState state)
    {
        var files = target.Entry is null ? state.Files.Select(file => new CustomizationFile(file.Name, file.Bytes)).ToArray()
            : [new CustomizationFile("server.json", new McpDefinitionDocument(state.Files.Single().Bytes).Read(target.Entry))];
        return new(target.Source, state.Revision, files);
    }
}