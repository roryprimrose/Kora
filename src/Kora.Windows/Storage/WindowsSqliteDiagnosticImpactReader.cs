using System.Text.Json;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed class WindowsSqliteDiagnosticImpactReader : IDiagnosticRetentionImpactReader, IDisposable
{
    private readonly Func<DiagnosticRetentionDays, DateTimeOffset, CancellationToken, DiagnosticImpactSnapshot> readProjection;
    private readonly Func<string> projectionStamp;
    private readonly FileSystemWatcher watcher;
    private long generation;
    private bool disposed;

    public WindowsSqliteDiagnosticImpactReader(WindowsSqliteEvidenceSink sink, IApplicationDataPaths paths)
        : this(sink.ReadDiagnosticImpact, sink.DiagnosticImpactStamp,
            Path.Combine(paths.LocalRoot, WindowsSqliteEvidenceSink.PartitionName), includeSubdirectories: false)
    {
    }

    public WindowsSqliteDiagnosticImpactReader(WindowsSegmentedEvidenceSink sink, IApplicationDataPaths paths)
        : this(sink.ReadDiagnosticImpact, sink.DiagnosticImpactStamp, paths.LocalRoot, includeSubdirectories: true)
    {
    }

    private WindowsSqliteDiagnosticImpactReader(
        Func<DiagnosticRetentionDays, DateTimeOffset, CancellationToken, DiagnosticImpactSnapshot> readProjection,
        Func<string> projectionStamp, string directory, bool includeSubdirectories)
    {
        this.readProjection = readProjection;
        this.projectionStamp = projectionStamp;
        watcher = new(directory)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
                | NotifyFilters.DirectoryName | NotifyFilters.Security,
            IncludeSubdirectories = includeSubdirectories,
        };
        watcher.Changed += OnChanged;
        watcher.Created += OnChanged;
        watcher.Deleted += OnChanged;
        watcher.Renamed += OnChanged;
        watcher.Error += OnError;
        try { watcher.EnableRaisingEvents = true; }
        catch { watcher.Dispose(); throw; }
    }

    public event EventHandler? Changed;
    private void OnChanged(object sender, FileSystemEventArgs args)
    {
        if (!string.Equals(Path.GetFileName(args.Name), "operation.lock", StringComparison.OrdinalIgnoreCase)) { Invalidate(); }
    }
    private void OnError(object sender, ErrorEventArgs args) => Invalidate();
    private void Invalidate()
    {
        Interlocked.Increment(ref generation);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public ValueTask<DiagnosticImpactSnapshot> ReadAsync(DiagnosticRetentionDays proposed, DateTimeOffset cutoff,
        HostRequest request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed), this);
        _ = new DiagnosticRetentionDays(proposed.Days);
        if (request.Origin != RequestOrigin.LocalUi)
        {
            throw new InvalidOperationException("Diagnostic impact requires an explicit native observation.");
        }
        var observed = Volatile.Read(ref generation);
        return new(Task.Run(() =>
        {
            try
            {
                var snapshot = readProjection(proposed, cutoff, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (observed != Volatile.Read(ref generation) || Volatile.Read(ref disposed)) { return DiagnosticImpactSnapshot.Unavailable; }
                return snapshot with { Snapshot = BindStamp(observed, snapshot.Snapshot!) };
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException
                or SqliteException or JsonException or ArgumentException or OverflowException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return DiagnosticImpactSnapshot.Unavailable;
            }
        }, cancellationToken));
    }

    public bool IsCurrent(DiagnosticImpactSnapshot snapshot)
    {
        if (Volatile.Read(ref disposed) || snapshot.Snapshot is null) { return false; }
        try
        {
            var observed = Volatile.Read(ref generation);
            var stamp = BindStamp(observed, projectionStamp());
            return !Volatile.Read(ref disposed) && observed == Volatile.Read(ref generation)
                && string.Equals(snapshot.Snapshot, stamp, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return false;
        }
    }

    private static string BindStamp(long observed, string stamp) =>
        observed.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + stamp;

    public void Dispose()
    {
        if (Volatile.Read(ref disposed)) { return; }
        Volatile.Write(ref disposed, true);
        watcher.Dispose();
        Invalidate();
    }
}
