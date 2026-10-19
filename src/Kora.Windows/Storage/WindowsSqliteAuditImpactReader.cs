using System.Text.Json;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed class WindowsSqliteAuditImpactReader : IAuditRetentionImpactReader, IDisposable
{
    private readonly WindowsSqliteHostInteractionStore authority;
    private readonly Func<AuditRetentionDays, DateTimeOffset, CancellationToken, AuditImpactResult> readProjection;
    private readonly Func<string> projectionStamp;
    private readonly FileSystemWatcher[] watchers;

    public WindowsSqliteAuditImpactReader(WindowsSqliteHostInteractionStore authority,
        WindowsSqliteEvidenceSink projection, IApplicationDataPaths paths)
        : this(authority, projection.ReadAuditImpact, projection.AuditImpactStamp,
            [Path.Combine(paths.LocalRoot, HostInteractionSchema.Partition),
                Path.Combine(paths.LocalRoot, WindowsSqliteEvidenceSink.PartitionName)], includeSubdirectories: false)
    {
    }

    public WindowsSqliteAuditImpactReader(WindowsSqliteHostInteractionStore authority,
        WindowsSegmentedEvidenceSink projection, IApplicationDataPaths paths)
        : this(authority, projection.ReadAuditImpact, projection.AuditImpactStamp,
            [paths.LocalRoot], includeSubdirectories: true)
    {
    }

    private WindowsSqliteAuditImpactReader(WindowsSqliteHostInteractionStore authority,
        Func<AuditRetentionDays, DateTimeOffset, CancellationToken, AuditImpactResult> readProjection,
        Func<string> projectionStamp, string[] directories, bool includeSubdirectories)
    {
        this.authority = authority;
        this.readProjection = readProjection;
        this.projectionStamp = projectionStamp;
        var created = new List<FileSystemWatcher>();
        try
        {
            foreach (var directory in directories)
            {
                var watcher = new FileSystemWatcher(directory)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
                        | NotifyFilters.DirectoryName | NotifyFilters.Security,
                    IncludeSubdirectories = includeSubdirectories,
                };
                created.Add(watcher);
                watcher.Changed += OnChanged;
                watcher.Created += OnChanged;
                watcher.Deleted += OnChanged;
                watcher.Renamed += OnChanged;
                watcher.Error += OnError;
                watcher.EnableRaisingEvents = true;
            }
            watchers = created.ToArray();
        }
        catch
        {
            foreach (var watcher in created) { watcher.Dispose(); }
            throw;
        }
    }

    public event EventHandler? Changed;
    private void OnChanged(object sender, FileSystemEventArgs args)
    {
        if (!string.Equals(Path.GetFileName(args.Name), "operation.lock", StringComparison.OrdinalIgnoreCase))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
    private void OnError(object sender, ErrorEventArgs args) => Changed?.Invoke(this, EventArgs.Empty);

    public ValueTask<AuditImpactResult> ReadAsync(AuditImpactSource source, AuditRetentionDays proposed,
        DateTimeOffset cutoff, HostRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(source) || request.Origin != RequestOrigin.LocalUi)
        {
            throw new InvalidOperationException("Audit impact is available only to an explicit native observation.");
        }
        return new(Task.Run(() =>
        {
            try
            {
                return source == AuditImpactSource.RequiredAuthority
                    ? authority.ReadAuditImpact(proposed, cutoff, cancellationToken)
                    : readProjection(proposed, cutoff, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or InvalidDataException or SqliteException or JsonException or ArgumentException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new AuditImpactResult(source, AuditImpactStatus.Unavailable, null, null);
            }
        }, cancellationToken));
    }

    public bool IsCurrent(AuditImpactResult result)
    {
        try
        {
            return result.Snapshot is { } stamp && string.Equals(stamp, result.Source == AuditImpactSource.RequiredAuthority
                ? authority.AuditImpactStamp() : projectionStamp(), StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        foreach (var watcher in watchers) { watcher.Dispose(); }
    }
}
