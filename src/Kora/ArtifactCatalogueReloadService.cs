using Kora.Application.Configuration;
using Kora.Application.Definitions;
using Kora.Core.Artifacts;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Definitions.Artifacts;
using Kora.Windows.Artifacts;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class ArtifactCatalogueReloadService : IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(300);
    private readonly ArtifactCatalogue catalogue;
    private readonly IApplicationDataPaths paths;
    private readonly IArtifactDirectoryPreferences preferences;
    private readonly ArtifactDirectoryConfigurationService configuration;
    private readonly McpServerConfigurationService mcpConfiguration;
    private readonly ILogger<ArtifactCatalogueReloadService> logger;
    private readonly InstructionRevisionCatalogue? instructionRevisions;
    private readonly WindowsOwnedInstructionStore? ownedInstructions;
    private readonly InstructionRevisionCatalogue? promptRevisions;
    private readonly WindowsOwnedInstructionStore? ownedPrompts;
    private readonly string? profileRoamingRoot;
    private readonly ArtifactUsePreferences? use;
    private readonly Lock gate = new();
    private readonly List<FileSystemWatcher> watchers = [];
    private Timer? timer;
    private ArtifactDirectorySettings? watchedSettings;
    private bool disposed;

    internal ArtifactCatalogueReloadService(
        ArtifactCatalogue catalogue,
        IApplicationDataPaths paths,
        IArtifactDirectoryPreferences preferences,
        ArtifactDirectoryConfigurationService configuration,
        McpServerConfigurationService mcpConfiguration,
        ILogger<ArtifactCatalogueReloadService> logger,
        InstructionRevisionCatalogue? instructionRevisions = null,
        WindowsOwnedInstructionStore? ownedInstructions = null,
        string? profileRoamingRoot = null,
        InstructionRevisionCatalogue? promptRevisions = null,
        WindowsOwnedInstructionStore? ownedPrompts = null,
        ArtifactUsePreferences? use = null)
    {
        this.catalogue = catalogue;
        this.paths = paths;
        this.preferences = preferences;
        this.configuration = configuration;
        this.mcpConfiguration = mcpConfiguration;
        this.logger = logger;
        this.instructionRevisions = instructionRevisions;
        this.ownedInstructions = ownedInstructions;
        this.profileRoamingRoot = profileRoamingRoot;
        this.promptRevisions = promptRevisions;
        this.ownedPrompts = ownedPrompts;
        this.use = use;
    }

    internal void Start()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (timer is not null)
            {
                return;
            }
            using (ExecutionContext.SuppressFlow())
            {
                timer = new(Reload, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
            configuration.Changed += OnConfigurationChanged;
            ConfigureWatchers();
        }
    }

    private void OnConfigurationChanged(object? sender, EventArgs args)
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            try
            {
                var current = preferences.Load();
                if (watchedSettings is { } previous && Enum.GetValues<ArtifactKind>().Any(kind =>
                    previous.GetDirectories(kind).Except(current.GetDirectories(kind), StringComparer.OrdinalIgnoreCase).Any()))
                {
                    // A removed source must not remain invocable if the replacement reload fails.
                    PublishReconciled(EmbeddedArtifactCatalogue.Load().Artifacts);
                }
                ConfigureWatchers();
                ScheduleReload();
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or ArgumentException
                or NotSupportedException)
            {
                ArtifactWatcherFailed(logger, exception.GetType().Name);
                PublishReconciled(EmbeddedArtifactCatalogue.Load().Artifacts);
                catalogue.ReportReloadFailure(exception);
            }
        }
    }

    private void OnDiskChanged(WatchTarget target, FileSystemEventArgs args, bool isDirectory)
    {
        if (!AffectsTarget(target, args.FullPath, isDirectory))
        {
            return;
        }

        lock (gate)
        {
            if (!disposed)
            {
                ScheduleReload();
            }
        }
    }

    private void OnDiskRenamed(WatchTarget target, RenamedEventArgs args, bool isDirectory)
    {
        if (!AffectsTarget(target, args.OldFullPath, isDirectory))
        {
            OnDiskChanged(target, args, isDirectory);
            return;
        }

        lock (gate)
        {
            if (!disposed)
            {
                ScheduleReload();
            }
        }
    }

    private void ScheduleReload() => timer!.Change(DebounceDelay, Timeout.InfiniteTimeSpan);

    internal bool ReloadNow()
    {
        var artifactCount = 0;
        Exception? failure = null;
        lock (gate)
        {
            if (disposed) { return false; }
            try
            {
                var settings = preferences.Load();
                var disk = Discovery(settings).Load();
                var embedded = EmbeddedArtifactCatalogue.Load();
                ArtifactDefinition[] candidates = [.. embedded.Artifacts, .. disk];
                PublishReconciled(candidates);
                artifactCount = catalogue.Artifacts.Count;
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or ArgumentException
                or NotSupportedException)
            {
                failure = exception;
                HoldOwnedInstructions();
            }
        }

        if (failure is not null)
        {
            ArtifactReloadFailed(logger, failure.GetType().Name);
            catalogue.ReportReloadFailure(failure);
            return false;
        }

        mcpConfiguration.NotifyChanged();
        ArtifactReloaded(logger, artifactCount);
        return true;
    }

    private void Reload(object? state) => ReloadNow();

    internal Task<bool> IsCurrentAsync(ArtifactDefinition selected, Func<bool> eligible, CancellationToken token) =>
        Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            lock (gate)
            {
                if (disposed || !eligible() || catalogue.IsReloadUnavailable
                    || !catalogue.IsCurrent(selected.Id, selected.Digest)) { return false; }
                try
                {
                    var disk = Discovery(preferences.Load()).Load();
                    token.ThrowIfCancellationRequested();
                    var exact = disk.SingleOrDefault(item =>
                        item.Kind == selected.Kind && string.Equals(item.UseControlId, selected.UseControlId, StringComparison.Ordinal));
                    if (exact is null || !string.Equals(exact.Id, selected.Id, StringComparison.Ordinal)
                        || !string.Equals(exact.Digest, selected.Digest, StringComparison.Ordinal))
                    { throw new InvalidDataException("The selected definition changed or disappeared. Reload and review its exact revision."); }
                    return eligible() && !disposed && catalogue.IsCurrent(selected.Id, selected.Digest);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                    or InvalidDataException or ArgumentException or NotSupportedException)
                {
                    ArtifactReloadFailed(logger, exception.GetType().Name);
                    HoldOwnedInstructions();
                    catalogue.ReportReloadFailure(exception);
                    return false;
                }
            }
        }, token);

    internal bool ReconcileSavedCopy(OwnedInstructionSnapshot saved)
    {
        lock (gate)
        {
            if (disposed || catalogue.IsReloadUnavailable) { return false; }
            var definition = saved.Document.Definition;
            var settings = preferences.Load();
            var include = settings.UseDefaultDirectories || settings.GetDirectories(definition.Kind).Contains(
                Path.Combine(paths.RoamingRoot, OwnedDefinitionKind.DirectoryName(definition.Kind)), StringComparer.OrdinalIgnoreCase);
            var retained = catalogue.SourceCandidates.Where(item => item.Kind != definition.Kind
                || !string.Equals(item.OwnedInstructionName, saved.Name, StringComparison.Ordinal));
            ArtifactDefinition[] candidates = include
                ? [.. retained, definition with { OwnedInstructionRevision = saved.Revision, IsUseEnabled = false }]
                : retained.ToArray();
            // Verified native readback enters the same revision pipeline without discovering profile originals
            // or re-reading a newer source in place of the frozen editor base.
            PublishReconciled(candidates);
            return true;
        }
    }

    internal bool ReconcileRenamedInstruction(OwnedInstructionSnapshot original, OwnedInstructionSnapshot saved)
    {
        lock (gate)
        {
            if (disposed || instructionRevisions is null) { return false; }
            var settings = preferences.Load();
            var sourceRoot = Path.Combine(paths.RoamingRoot, "Instructions");
            var include = settings.UseDefaultDirectories
                || settings.GetDirectories(ArtifactKind.Instruction).Contains(sourceRoot, StringComparer.OrdinalIgnoreCase);
            var retained = catalogue.SourceCandidates.Where(item => item.Kind != ArtifactKind.Instruction
                || !string.Equals(item.OwnedInstructionName, original.Name, StringComparison.Ordinal)
                    && !string.Equals(item.OwnedInstructionName, saved.Name, StringComparison.Ordinal));
            ArtifactDefinition[] candidates = include
                ? [.. retained, saved.Document.Definition with
                {
                    OwnedInstructionRevision = saved.Revision, IsUseEnabled = false,
                    UseControlId = ArtifactUseIdentity.File(Path.Combine(sourceRoot, saved.CanonicalFileName)),
                }]
                : retained.ToArray();
            PublishReconciled(candidates);
            return !catalogue.Candidates.Any(item => item.Kind == ArtifactKind.Instruction
                    && string.Equals(item.OwnedInstructionName, original.Name, StringComparison.Ordinal))
                && !catalogue.Artifacts.Any(item => item.Kind == ArtifactKind.Instruction
                    && string.Equals(item.OwnedInstructionName, saved.Name, StringComparison.Ordinal));
        }
    }

    private void PublishReconciled(IReadOnlyList<ArtifactDefinition> candidates)
    {
        _ = new ArtifactCatalogue(candidates);
        use?.Reconcile(candidates);
        void PublishPrompts(IReadOnlyList<ArtifactDefinition> instructions)
        {
            if (promptRevisions is null) { catalogue.Publish(instructions); }
            else { promptRevisions.PublishReconciled(instructions, catalogue.Publish); }
        }
        if (instructionRevisions is null) { PublishPrompts(candidates); }
        else { instructionRevisions.PublishReconciled(candidates, PublishPrompts); }
    }

    private void ConfigureWatchers()
    {
        foreach (var watcher in watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        watchers.Clear();

        var settings = preferences.Load();
        watchedSettings = settings;
        var discovery = Discovery(settings);
        var targets = discovery.WatchRoots
            .Select(root => new WatchTarget(
                root.Path, IsFile: false, root.Patterns, root.MaximumDepth, root.WatchAllFiles))
            .Concat(mcpConfiguration.SourceFiles.Select(path =>
                new WatchTarget(path, IsFile: true, [], MaximumDepth: 0, WatchAllFiles: false)))
            .Distinct()
            .ToArray();
        foreach (var target in targets)
        {
            var desiredDirectory = target.IsFile ? Path.GetDirectoryName(target.Path) : target.Path;
            var directory = FindExistingDirectory(desiredDirectory);
            if (directory is null)
            {
                continue;
            }
            AddWatcher(target, directory, desiredDirectory, isDirectory: false);
            AddWatcher(target, directory, desiredDirectory, isDirectory: true);
        }
    }

    private void AddWatcher(WatchTarget target, string directory, string? desiredDirectory, bool isDirectory)
    {
        // DirectoryName-only notifications retain the event's kind even after a rename or deletion.
        // No source enumeration or filename-extension heuristic is needed to classify a directory.
        var watcher = new FileSystemWatcher(directory)
        {
            Filter = !isDirectory && target.IsFile && string.Equals(
                directory, desiredDirectory, StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileName(target.Path)
                    : "*",
            IncludeSubdirectories = !target.IsFile
                || !string.Equals(directory, desiredDirectory, StringComparison.OrdinalIgnoreCase),
            NotifyFilter = isDirectory ? NotifyFilters.DirectoryName
                : NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size,
        };
        watchers.Add(watcher);
        watcher.Changed += (_, args) => OnDiskChanged(target, args, isDirectory);
        watcher.Created += (_, args) => OnDiskChanged(target, args, isDirectory);
        watcher.Deleted += (_, args) => OnDiskChanged(target, args, isDirectory);
        watcher.Renamed += (_, args) => OnDiskRenamed(target, args, isDirectory);
        watcher.Error += OnWatcherError;
        watcher.EnableRaisingEvents = true;
    }

    private static bool AffectsTarget(WatchTarget target, string changedPath, bool isDirectory)
    {
        var expected = Path.GetFullPath(target.Path);
        var changed = Path.GetFullPath(changedPath);
        if (string.Equals(expected, changed, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var separator = Path.DirectorySeparatorChar.ToString();
        if (isDirectory && expected.StartsWith(changed.TrimEnd(Path.DirectorySeparatorChar) + separator,
            StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (target.IsFile
            || !changed.StartsWith(expected.TrimEnd(Path.DirectorySeparatorChar) + separator,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relative = Path.GetRelativePath(expected, changed);
        var segments = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return true;
        }

        if (isDirectory)
        {
            return segments.Length <= target.MaximumDepth;
        }

        var fileName = segments[^1];
        var fileDepth = segments.Length - 1;
        return fileDepth <= target.MaximumDepth
            && (target.WatchAllFiles || target.Patterns.Any(pattern =>
                System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(
                    pattern, fileName, ignoreCase: true)));
    }

    private static string? FindExistingDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }
        while (!string.IsNullOrWhiteSpace(path))
        {
            if (Directory.Exists(path))
            {
                return path;
            }
            path = Path.GetDirectoryName(path);
        }
        return null;
    }

    private void OnWatcherError(object sender, ErrorEventArgs args)
    {
        lock (gate)
        {
            if (disposed) { return; }
            var exception = args.GetException();
            ArtifactWatcherFailed(logger, exception.GetType().Name);
            HoldOwnedInstructions();
            catalogue.ReportReloadFailure(exception);
            try
            {
                ConfigureWatchers();
                ScheduleReload();
            }
            catch (Exception failure) when (failure is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or ArgumentException
                or NotSupportedException)
            {
                ArtifactWatcherFailed(logger, failure.GetType().Name);
                catalogue.ReportReloadFailure(failure);
            }
        }
    }

    public void Dispose()
    {
        Timer? pending;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            configuration.Changed -= OnConfigurationChanged;
            foreach (var watcher in watchers)
            {
                watcher.Dispose();
            }
            watchers.Clear();
            pending = timer;
            timer = null;
        }
        if (pending is not null)
        {
            using var completed = new ManualResetEvent(false);
            if (pending.Dispose(completed)) { completed.WaitOne(); }
        }
    }

    private sealed record WatchTarget(
        string Path,
        bool IsFile,
        IReadOnlyList<string> Patterns,
        int MaximumDepth,
        bool WatchAllFiles);

    private WindowsDiskArtifactDiscovery Discovery(ArtifactDirectorySettings settings) =>
        new(paths, settings, ownedInstructions, profileRoamingRoot, ownedPrompts);

    private void HoldOwnedInstructions()
    {
        catalogue.Publish(catalogue.Candidates.Select(item => item.OwnedInstructionName is not null
            ? item with { IsUseEnabled = false } : item).ToArray());
        try { instructionRevisions?.MarkUnavailable(); }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or InvalidDataException)
        { ArtifactReloadFailed(logger, failure.GetType().Name); }
        try { promptRevisions?.MarkUnavailable(); }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or InvalidDataException)
        { ArtifactReloadFailed(logger, failure.GetType().Name); }
        try { use?.Reconcile([]); }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        { ArtifactReloadFailed(logger, failure.GetType().Name); }
    }
}
