using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Kora.Core.Configuration;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed class WindowsSqliteSessionRetentionPlannerReader : ISessionRetentionPlannerReader, IDisposable
{
    private readonly WindowsSqliteHostInteractionStore store;
    private readonly FileSystemWatcher watcher;
    private readonly string authorityDirectory;
    private readonly string preferenceDirectory;
    private long revision;
    private int disposed;
    private int sourceLost;

    public WindowsSqliteSessionRetentionPlannerReader(WindowsSqliteHostInteractionStore store, IApplicationDataPaths paths)
    {
        this.store = store;
        authorityDirectory = Path.Combine(paths.LocalRoot, HostInteractionSchema.Partition);
        preferenceDirectory = Path.Combine(paths.LocalRoot, "Preferences");
        watcher = new(paths.LocalRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
                | NotifyFilters.DirectoryName | NotifyFilters.Security,
        };
        watcher.Changed += OnChanged;
        watcher.Created += OnChanged;
        watcher.Deleted += OnChanged;
        watcher.Renamed += OnRenamed;
        watcher.Error += OnError;
        watcher.EnableRaisingEvents = true;
    }

    public event EventHandler? Changed;

    private bool Relevant(string path) =>
        string.Equals(path, authorityDirectory, StringComparison.OrdinalIgnoreCase)
        || string.Equals(path, preferenceDirectory, StringComparison.OrdinalIgnoreCase)
        || string.Equals(Path.GetDirectoryName(path), authorityDirectory, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Path.GetFileName(path), "operation.lock", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Path.GetDirectoryName(path), preferenceDirectory, StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(path).StartsWith("session-retention", StringComparison.OrdinalIgnoreCase);

    private void OnChanged(object sender, FileSystemEventArgs args)
    {
        if (Relevant(args.FullPath)) { Signal(); }
    }

    private void OnRenamed(object sender, RenamedEventArgs args)
    {
        if (Relevant(args.FullPath) || Relevant(args.OldFullPath)) { Signal(); }
    }

    private void OnError(object sender, ErrorEventArgs args)
    {
        Interlocked.Exchange(ref sourceLost, 1);
        Signal();
    }

    private void Signal()
    {
        Interlocked.Increment(ref revision);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private string Stamp(string authority, long expectedRevision)
    {
        if (Volatile.Read(ref disposed) != 0 || Volatile.Read(ref sourceLost) != 0
            || Interlocked.Read(ref revision) != expectedRevision)
        {
            throw new InvalidDataException("The retention planner source lifetime changed or became unavailable.");
        }
        var parts = new List<string> { authority, expectedRevision.ToString(CultureInfo.InvariantCulture) };
        foreach (var name in new[] { "session-retention.txt", "session-retention-unconfirmed.txt" })
        {
            var path = Path.Combine(preferenceDirectory, name);
            try
            {
                var attributes = File.GetAttributes(path);
                if (attributes.HasFlag(FileAttributes.Directory) || attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new InvalidDataException("The retention preference metadata is not an ordinary local file.");
                }
                var file = new FileInfo(path);
                parts.Add(string.Join(':', file.Length, file.CreationTimeUtc.Ticks, file.LastWriteTimeUtc.Ticks));
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                parts.Add("absent");
            }
        }
        if (Interlocked.Read(ref revision) != expectedRevision)
        {
            throw new InvalidDataException("The retention preference source changed during observation.");
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', parts))));
    }

    public ValueTask<SessionRetentionPlanResult> ReadAsync(SessionRetentionSettings proposed, DateTimeOffset cutoff,
        HostRequest request, CancellationToken cancellationToken)
    {
        proposed.Validate();
        if (request.Origin != RequestOrigin.LocalUi || request.InvocationId is not null)
        {
            throw new InvalidOperationException("Retention planning requires an explicit native observation, without invocation authority.");
        }
        return new(Task.Run(() =>
        {
            using var timeout = new CancellationTokenSource(SessionRetentionPlanner.ScanTimeout);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            var token = cancellation.Token;
            var observedRevision = Interlocked.Read(ref revision);
            try
            {
                try
                {
                    var initial = Stamp(store.RetentionPlannerStamp(token), observedRevision);
                    var result = store.ReadRetentionPlan(proposed, cutoff, token);
                    var stamp = Stamp(result.Snapshot!, observedRevision);
                    var current = Stamp(store.RetentionPlannerStamp(token), observedRevision);
                    if (!string.Equals(initial, stamp, StringComparison.Ordinal)
                        || !string.Equals(stamp, current, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException("The retention source changed before the planner receipt.");
                    }
                    token.ThrowIfCancellationRequested();
                    return result with { Snapshot = stamp };
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                    or InvalidDataException or SqliteException or JsonException or ArgumentException or OverflowException or FormatException)
                {
                    token.ThrowIfCancellationRequested();
                    return new SessionRetentionPlanResult(SessionRetentionPlanStatus.Unavailable, null, null);
                }
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
            {
                throw new TimeoutException("The bounded retention planner source timed out.", exception);
            }
        }, cancellationToken));
    }

    public bool IsCurrent(SessionRetentionPlanResult result)
    {
        try
        {
            var observedRevision = Interlocked.Read(ref revision);
            return result.Snapshot is { } snapshot && string.Equals(snapshot,
                Stamp(store.RetentionPlannerStamp(CancellationToken.None), observedRevision), StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) { return; }
        watcher.Dispose();
        Signal();
    }
}
