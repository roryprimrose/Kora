using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Kora.Rt1;

namespace Kora.Rt2;

internal sealed record ObservedProcess(int Id, int ParentId, DateTime StartUtc, string Role, string ImageName, string ImageClass);
internal sealed record FileNotice(DateTimeOffset Utc, string Kind, string RelativePath);
internal sealed record PhaseNotice(DateTimeOffset Utc, string Phase);
internal sealed record FileReceipt(string RelativePath, long Bytes, string Sha256, bool DeniedMarker,
    bool CredentialMarker, bool DiscoveryCanary);

internal sealed class LifecycleObserver : IAsyncDisposable
{
    private readonly string root;
    private readonly int hostId = Environment.ProcessId;
    private readonly CancellationTokenSource stop = new();
    private readonly FileSystemWatcher watcher;
    private readonly DiagnosticObserver diagnostics = new();
    private readonly ConcurrentDictionary<int, ObservedProcess> owned = new();
    private readonly ConcurrentDictionary<string, string> imagePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SocketRow> sockets = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> modules = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<FileNotice> files = new();
    private readonly ConcurrentQueue<string> gaps = new();
    private readonly ConcurrentQueue<PhaseNotice> phases = new();
    private Task? loop;
    private long lastSample;
    private double maximumSampleGapMs;
    private int samples;
    private int fileOverflow;
    private int disposed;
    internal DateTimeOffset StartUtc { get; } = DateTimeOffset.UtcNow;
    internal DateTimeOffset EndUtc { get; private set; }
    internal bool RuntimeSeen => owned.Values.Any(value => value.Role == "runtime");
    internal IReadOnlyCollection<ObservedProcess> Processes => owned.Values.ToArray();

    internal LifecycleObserver(string root)
    {
        this.root = Path.GetFullPath(root);
        Directory.CreateDirectory(root);
        watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize = 64 * 1024
        };
        watcher.Created += (_, args) => Notice("create", args.FullPath);
        watcher.Changed += (_, args) => Notice("change", args.FullPath);
        watcher.Deleted += (_, args) => Notice("delete", args.FullPath);
        watcher.Renamed += (_, args) => Notice("rename", args.FullPath);
        watcher.Error += (_, _) => Interlocked.Increment(ref fileOverflow);
    }

    private void Notice(string kind, string path) =>
        files.Enqueue(new FileNotice(DateTimeOffset.UtcNow, kind, Path.GetRelativePath(root, path)));

    internal void MarkPhase(string phase) => phases.Enqueue(new PhaseNotice(DateTimeOffset.UtcNow, phase));

    internal void Start()
    {
        watcher.EnableRaisingEvents = true;
        Sample();
        loop = LoopAsync();
    }

    internal static bool IsOwnedRoot(int parentId, int hostId, string? image, string expected) =>
        parentId == hostId && image is not null
        && Path.GetFullPath(image).Equals(Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase);

    private async Task LoopAsync()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20), stop.Token);
                Sample();
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }

    internal void Sample()
    {
        var now = Stopwatch.GetTimestamp();
        if (lastSample != 0)
            maximumSampleGapMs = Math.Max(maximumSampleGapMs, Stopwatch.GetElapsedTime(lastSample, now).TotalMilliseconds);
        lastSample = now;
        samples++;
        var snapshot = NativeSnapshot.Processes();
        foreach (var row in snapshot)
        {
            if (owned.ContainsKey(row.Id) || (row.ParentId != hostId && !owned.ContainsKey(row.ParentId))) continue;
            try
            {
                using var process = Process.GetProcessById(row.Id);
                var image = process.MainModule?.FileName;
                var runtime = IsOwnedRoot(row.ParentId, hostId, image, Candidate.Executable);
                if (!runtime && (!owned.TryGetValue(row.ParentId, out var parent) || !IsLive(parent))) continue;
                var start = process.StartTime.ToUniversalTime();
                if (start < StartUtc.UtcDateTime.AddSeconds(-1)) continue;
                var classification = runtime ? "approved-runtime"
                    : image is not null && image.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + '\\', StringComparison.OrdinalIgnoreCase)
                        ? "windows-helper" : "unreviewed-helper";
                var name = image is null ? "unknown" : Path.GetFileName(image);
                owned.TryAdd(row.Id, new ObservedProcess(row.Id, row.ParentId, start, runtime ? "runtime" : "descendant", name, classification));
                if (image is not null) imagePaths.TryAdd(classification + ":" + name, image);
            }
            catch (ArgumentException) { gaps.Enqueue("process-exited-before-identity-read"); }
            catch (InvalidOperationException) { gaps.Enqueue("process-exited-before-image-read"); }
            catch (Win32Exception error) { gaps.Enqueue("process-query-error-" + error.NativeErrorCode); }
        }
        var live = new HashSet<int> { hostId };
        foreach (var identity in owned.Values)
        {
            try
            {
                using var process = Process.GetProcessById(identity.Id);
                if (process.HasExited || process.StartTime.ToUniversalTime() != identity.StartUtc) continue;
                live.Add(identity.Id);
                foreach (ProcessModule module in process.Modules)
                {
                    var path = module.FileName;
                    var classification = path.StartsWith(Candidate.RuntimeDirectory + '\\', StringComparison.OrdinalIgnoreCase)
                        ? "approved-runtime:" + Path.GetFileName(path)
                        : path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + '\\', StringComparison.OrdinalIgnoreCase)
                            ? "windows:" + Path.GetFileName(path) : "outside-runtime-or-windows";
                    modules.TryAdd(identity.Id + ":" + classification, classification);
                }
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { gaps.Enqueue("module-query-raced-exit"); }
            catch (Win32Exception error) { gaps.Enqueue("module-query-error-" + error.NativeErrorCode); }
        }
        foreach (var row in NativeSnapshot.Sockets().Where(row => live.Contains(row.ProcessId)))
        {
            var role = row.ProcessId == hostId ? "host" : "runtime-or-descendant";
            sockets.TryAdd(role + ":" + JsonSerializer.Serialize(row), row);
        }
    }

    internal async Task WaitForRuntimeAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!RuntimeSeen) await Task.Delay(TimeSpan.FromMilliseconds(20), deadline.Token);
    }

    internal async Task<FileReceipt[]> InspectRecoverableFilesAsync()
    {
        var receipts = new List<FileReceipt>();
        foreach (var path in EnumerateOwnedFiles().Order(StringComparer.Ordinal))
        {
            var info = new FileInfo(path);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.Length > 1_048_576)
                throw new InvalidDataException("Uninspectable owned scratch file.");
            var text = await File.ReadAllTextAsync(path);
            receipts.Add(new FileReceipt(Path.GetRelativePath(root, path), info.Length, await Candidate.HashAsync(path),
                text.Contains(Candidate.Denied, StringComparison.Ordinal),
                text.Contains(Candidate.Credential, StringComparison.Ordinal),
                text.Contains(Candidate.Collected, StringComparison.Ordinal)));
        }
        return receipts.ToArray();
    }

    private IEnumerable<string> EnumerateOwnedFiles()
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Refusing owned scratch inspection through a reparse point.");
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Uninspectable reparse entry in owned scratch.");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                else yield return entry;
            }
        }
    }

    internal async Task<IReadOnlyDictionary<string, string>> ImageHashesAsync()
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (identity, path) in imagePaths)
            hashes.Add(identity, await Candidate.HashAsync(path));
        return hashes;
    }

    internal int LiveOwnedProcesses()
    {
        var live = 0;
        foreach (var identity in owned.Values)
        {
            try
            {
                using var process = Process.GetProcessById(identity.Id);
                if (!process.HasExited && process.StartTime.ToUniversalTime() == identity.StartUtc) live++;
            }
            catch (ArgumentException) { }
        }
        return live;
    }

    private static bool IsLive(ObservedProcess identity)
    {
        try
        {
            using var process = Process.GetProcessById(identity.Id);
            return !process.HasExited && process.StartTime.ToUniversalTime() == identity.StartUtc;
        }
        catch (ArgumentException) { return false; }
    }

    internal object Receipt() => new
    {
        StartUtc, EndUtc, TargetIntervalMs = 20, MaximumSampleGapMs = maximumSampleGapMs, Samples = samples,
        Processes = owned.Values.OrderBy(value => value.Id).ToArray(),
        SocketSnapshots = sockets.Values.OrderBy(value => value.ProcessId).ThenBy(value => value.LocalPort).ToArray(),
        ModuleSnapshots = modules.Keys.Order(StringComparer.Ordinal).ToArray(),
        FileNotifications = files.ToArray(), FileOverflow = fileOverflow, QueryGaps = gaps.Distinct().ToArray(),
        ManagedDiagnosticEventCounts = diagnostics.Counts, DiagnosticEmitterProcessId = hostId,
        Phases = phases.ToArray(),
        FileProcessAttribution = "BLOCKED: ReadDirectoryChangesW does not identify writer PID",
        AllDestinations = "BLOCKED: snapshots miss short-lived TCP; UDP destinations, DNS, ICMP, named pipes and non-IP transports unobserved",
        NativeDiagnostics = "BLOCKED: approved RT1 log-none profile; stdio protocol callbacks visible, native stderr/ETW/file diagnostics not independently captured",
        Enforcement = "None from observer; only original RT1 public model-request and volatile-session-I/O boundaries mediate their paths",
        BlindSpots = "Outside-scratch writes, deleted/transient contents, ADS, registry, pagefile/crash dump, pre-sample descendants/images and outliving/detached descendants cannot be ruled out"
    };

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        await stop.CancelAsync();
        try { if (loop is not null) await loop; }
        finally
        {
            EndUtc = DateTimeOffset.UtcNow;
            watcher.Dispose();
            diagnostics.Dispose();
            stop.Dispose();
        }
    }
}
