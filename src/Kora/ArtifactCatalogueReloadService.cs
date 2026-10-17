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
    private readonly Lock gate = new();
    private readonly List<FileSystemWatcher> watchers = [];
    private Timer? timer;
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
        WindowsOwnedInstructionStore? ownedPrompts = null)
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
                HoldOwnedInstructions();
                catalogue.ReportReloadFailure(exception);
            }
        }
    }

    private void OnDiskChanged(WatchTarget target, FileSystemEventArgs args)
    {
        if (!AffectsTarget(target, args.FullPath, includeDirectory:
            args.ChangeType != WatcherChangeTypes.Changed))
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

    private void OnDiskRenamed(WatchTarget target, RenamedEventArgs args)
    {
        if (!AffectsTarget(target, args.OldFullPath, includeDirectory: true))
        {
            OnDiskChanged(target, args);
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

    internal bool ReconcileSavedCopy(OwnedInstructionSnapshot saved)
    {
        lock (gate)
        {
            if (disposed) { return false; }
            var definition = saved.Document.Definition;
            ArtifactDefinition[] candidates = [.. catalogue.Candidates.Where(item =>
                item.Kind != definition.Kind || !string.Equals(item.OwnedInstructionName, saved.Name, StringComparison.Ordinal)),
                definition with { OwnedInstructionRevision = saved.Revision, IsUseEnabled = false }];
            // Verified native readback enters the same revision pipeline without discovering profile originals
            // or re-reading a newer source in place of the frozen editor base.
            PublishReconciled(candidates);
            return true;
        }
    }

    private void PublishReconciled(IReadOnlyList<ArtifactDefinition> candidates)
    {
        _ = new ArtifactCatalogue(candidates);
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
            var watcher = new FileSystemWatcher(directory)
            {
                Filter = target.IsFile && string.Equals(
                    directory,
                    Path.GetDirectoryName(target.Path),
                    StringComparison.OrdinalIgnoreCase)
                        ? Path.GetFileName(target.Path)
                        : "*",
                IncludeSubdirectories = !target.IsFile
                    || !string.Equals(directory, desiredDirectory, StringComparison.OrdinalIgnoreCase),
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.CreationTime
                    | NotifyFilters.Size,
            };
            watcher.Changed += (_, args) => OnDiskChanged(target, args);
            watcher.Created += (_, args) => OnDiskChanged(target, args);
            watcher.Deleted += (_, args) => OnDiskChanged(target, args);
            watcher.Renamed += (_, args) => OnDiskRenamed(target, args);
            watcher.Error += OnWatcherError;
            watcher.EnableRaisingEvents = true;
            watchers.Add(watcher);
        }
    }

    private static bool AffectsTarget(WatchTarget target, string changedPath, bool includeDirectory)
    {
        var expected = Path.GetFullPath(target.Path);
        var changed = Path.GetFullPath(changedPath);
        if (string.Equals(expected, changed, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var separator = Path.DirectorySeparatorChar.ToString();
        if (expected.StartsWith(changed.TrimEnd(Path.DirectorySeparatorChar) + separator,
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

        var fileName = segments[^1];
        var fileDepth = segments.Length - 1;
        if (fileDepth <= target.MaximumDepth
            && (target.WatchAllFiles || target.Patterns.Any(pattern =>
                System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(
                    pattern, fileName, ignoreCase: true))))
        {
            return true;
        }

        return includeDirectory
            && !Path.HasExtension(fileName)
            && segments.Length <= target.MaximumDepth;
    }

    private static string? FindExistingDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }
        if (Directory.Exists(path))
        {
            return path;
        }
        var parent = Path.GetDirectoryName(path);
        return !string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent)
            ? parent
            : null;
    }

    private void OnWatcherError(object sender, ErrorEventArgs args)
    {
        var exception = args.GetException();
        ArtifactWatcherFailed(logger, exception.GetType().Name);
        HoldOwnedInstructions();
        catalogue.ReportReloadFailure(exception);
        lock (gate)
        {
            if (!disposed)
            {
                ConfigureWatchers();
                ScheduleReload();
            }
        }
    }

    public void Dispose()
    {
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
            timer?.Dispose();
            timer = null;
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
    }
}
