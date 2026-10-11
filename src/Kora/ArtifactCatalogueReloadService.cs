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
        string? profileRoamingRoot = null)
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
            timer = new(Reload, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
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

    private void OnDiskChanged(object sender, FileSystemEventArgs args)
    {
        lock (gate)
        {
            if (!disposed)
            {
                ScheduleReload();
            }
        }
    }

    private void OnDiskRenamed(object sender, RenamedEventArgs args) => OnDiskChanged(sender, args);

    private void ScheduleReload() => timer!.Change(DebounceDelay, Timeout.InfiniteTimeSpan);

    internal bool ReloadNow()
    {
        lock (gate)
        {
        if (disposed) { return false; }
        try
        {
            var settings = preferences.Load();
            var disk = Discovery(settings).Load();
            var embedded = EmbeddedArtifactCatalogue.Load();
            ArtifactDefinition[] candidates = [.. embedded.Artifacts, .. disk];
            catalogue.Publish(instructionRevisions?.Reconcile(candidates) ?? candidates);
            mcpConfiguration.NotifyChanged();
            ArtifactReloaded(logger, catalogue.Artifacts.Count);
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or ArgumentException
            or NotSupportedException)
        {
            ArtifactReloadFailed(logger, exception.GetType().Name);
            HoldOwnedInstructions();
            catalogue.ReportReloadFailure(exception);
            return false;
        }
        }
    }

    private void Reload(object? state) => ReloadNow();

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
        var targets = discovery.RootPaths
            .Select(path => new WatchTarget(path, IsFile: false))
            .Concat(mcpConfiguration.SourceFiles.Select(path => new WatchTarget(path, IsFile: true)))
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
            watcher.Changed += OnDiskChanged;
            watcher.Created += OnDiskChanged;
            watcher.Deleted += OnDiskChanged;
            watcher.Renamed += OnDiskRenamed;
            watcher.Error += OnWatcherError;
            watcher.EnableRaisingEvents = true;
            watchers.Add(watcher);
        }
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

    private sealed record WatchTarget(string Path, bool IsFile);

    private WindowsDiskArtifactDiscovery Discovery(ArtifactDirectorySettings settings) =>
        new(paths, settings, ownedInstructions, profileRoamingRoot);

    private void HoldOwnedInstructions()
    {
        catalogue.Publish(catalogue.Candidates.Select(item => item.OwnedInstructionName is not null
            ? item with { IsUseEnabled = false } : item).ToArray());
        try { instructionRevisions?.MarkUnavailable(); }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or InvalidDataException)
        { ArtifactReloadFailed(logger, failure.GetType().Name); }
    }
}
