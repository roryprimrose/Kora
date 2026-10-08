using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using Kora.Rt2;

namespace R02Proof;

internal sealed record ProcessIdentity(int Id, long StartedUtcTicks);
internal sealed record ProcessResourceSample(
    ProcessIdentity Identity, int ParentId, int SessionId, string Name,
    long WorkingSetBytes, long PrivateBytes, double CpuSeconds);
internal sealed record ProcessResourceFrame(
    DateTime CapturedUtc, bool RootListed, ProcessResourceSample[] Processes, string[] Errors);
internal enum ProcessRootState { Observed, Exited, Reused, Unavailable }
internal sealed record ProcessSelection(
    ProcessRootState RootState, ProcessResourceSample[] Processes, string[] Errors);

internal interface IProcessResourceSource
{
    ProcessResourceFrame Capture(ProcessIdentity root, IReadOnlyCollection<ProcessIdentity> known);
    void VerifyListener(ProcessIdentity root);
}

internal sealed class ProcessLineage(ProcessIdentity root)
{
    private readonly HashSet<ProcessIdentity> known = [root];
    public IReadOnlyCollection<ProcessIdentity> Known => known.ToArray();

    public ProcessSelection Select(ProcessResourceFrame frame)
    {
        var current = frame.Processes.ToDictionary(p => p.Identity.Id);
        var errors = new List<string>(frame.Errors);
        current.TryGetValue(root.Id, out var actualRoot);
        var state = actualRoot is null
            ? frame.RootListed ? ProcessRootState.Unavailable : ProcessRootState.Exited
            : actualRoot.Identity == root ? ProcessRootState.Observed : ProcessRootState.Reused;
        var selected = current.Values.Where(p => known.Contains(p.Identity) && p.Identity != root)
            .ToDictionary(p => p.Identity.Id);
        if (state == ProcessRootState.Observed) selected.Add(root.Id, actualRoot!);
        else errors.Add($"Admitted root {root.Id} is {state}; identity continuity is not established.");
        foreach (var previous in known.Where(p => p != root))
        {
            if (!current.TryGetValue(previous.Id, out var replacement))
                errors.Add($"Previously observed process {previous.Id} absent or unreadable; final CPU/termination time is unknown.");
            else if (replacement.Identity != previous)
                errors.Add($"Previously observed process {previous.Id} PID reused; replacement requires fresh lineage admission.");
        }

        // Previously admitted orphans remain attributable, but a reused PID never inherits admission.
        bool added;
        do
        {
            added = false;
            foreach (var child in current.Values)
            {
                if (selected.ContainsKey(child.Identity.Id) || child.Identity == root
                    || !selected.TryGetValue(child.ParentId, out var parent))
                    continue;
                if (child.Identity.StartedUtcTicks < parent.Identity.StartedUtcTicks
                    || child.Identity.StartedUtcTicks > frame.CapturedUtc.Ticks)
                {
                    errors.Add($"Process {child.Identity.Id} has an inconsistent parent/snapshot creation time; not admitted.");
                    continue;
                }
                if (state != ProcessRootState.Observed && !known.Contains(child.Identity)) continue;
                selected.Add(child.Identity.Id, child);
                known.Add(child.Identity);
                added = true;
            }
        } while (added);
        return new(state, selected.Values.OrderBy(p => p.Identity.Id).ToArray(), errors.Distinct().ToArray());
    }
}

internal sealed class ProcessTreeObserver
{
    private readonly IProcessResourceSource source;
    private readonly ProcessLineage lineage;
    private readonly object gate = new();
    public ProcessIdentity Root { get; }

    internal ProcessTreeObserver(ProcessIdentity root, IProcessResourceSource source)
    {
        if (root.Id <= 0 || root.StartedUtcTicks <= 0)
            throw new ArgumentException("A positive PID and observed process creation time are required.", nameof(root));
        Root = root;
        this.source = source;
        lineage = new(root);
    }

    public static ProcessTreeObserver Admit(int serverProcessId)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Native process observation requires Windows.");
        using var process = Process.GetProcessById(serverProcessId);
        if (!string.Equals(process.ProcessName, "ollama", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The explicitly selected server PID is not an Ollama process.");
        var root = new ProcessIdentity(serverProcessId, process.StartTime.ToUniversalTime().Ticks);
        var observer = new ProcessTreeObserver(root, new WindowsProcessResourceSource());
        observer.VerifyListener();
        return observer;
    }

    public void VerifyListener()
    {
        lock (gate) source.VerifyListener(Root);
    }

    public ProcessSelection Capture()
    {
        lock (gate) return lineage.Select(source.Capture(Root, lineage.Known));
    }
}

internal sealed class WindowsProcessResourceSource : IProcessResourceSource
{
    public void VerifyListener(ProcessIdentity root)
    {
        try
        {
            using var process = Process.GetProcessById(root.Id);
            if (process.StartTime.ToUniversalTime().Ticks != root.StartedUtcTicks || process.HasExited)
                throw new InvalidOperationException("The admitted server process exited or its PID was reused; no request may be forwarded.");
            ValidateListeners(root, NativeSnapshot.Sockets(), Environment.ProcessId);
            if (process.HasExited)
                throw new InvalidOperationException("The admitted server exited during listener verification; no request may be forwarded.");
        }
        catch (Exception exception) when (exception is Win32Exception or ArgumentException)
        {
            throw new InvalidOperationException("Could not verify admitted server/listener identity; no request may be forwarded.", exception);
        }
    }

    internal static void ValidateListeners(ProcessIdentity root, IEnumerable<SocketRow> sockets, int clientProcessId)
    {
        var snapshot = sockets.ToArray();
        var listeners = snapshot.Where(s => s.Protocol == "TCP" && s.LocalPort == 11434 && s.State == 2).ToArray();
        if (listeners.Length == 0 || listeners.Any(s => s.ProcessId != root.Id
            || !IPAddress.IsLoopback(IPAddress.Parse(s.LocalAddress))))
            throw new InvalidOperationException("The loopback listener is missing, changed, shared or non-loopback; no request may be forwarded.");
        if (snapshot.Any(s => s.Protocol == "TCP" && s.RemotePort == 11434
            && s.State is 3 or 4 or 5 or 8 && s.ProcessId != clientProcessId))
            throw new InvalidOperationException("Another active TCP client was observed; exclusive use is not established. No request may be forwarded.");
    }

    public ProcessResourceFrame Capture(ProcessIdentity root, IReadOnlyCollection<ProcessIdentity> known)
    {
        var capturedUtc = DateTime.UtcNow;
        var topology = NativeSnapshot.Processes().ToDictionary(p => p.Id);
        var candidates = known.Select(p => p.Id).ToHashSet();
        candidates.Add(root.Id);
        bool added;
        do
        {
            added = false;
            foreach (var row in topology.Values)
                if (candidates.Contains(row.ParentId) && candidates.Add(row.Id)) added = true;
        } while (added);
        var samples = new List<ProcessResourceSample>();
        var errors = new List<string>();
        foreach (var id in candidates.Order())
        {
            if (!topology.TryGetValue(id, out var row)) continue;
            try
            {
                using var process = Process.GetProcessById(id);
                var identity = new ProcessIdentity(id, process.StartTime.ToUniversalTime().Ticks);
                if (identity.StartedUtcTicks > capturedUtc.Ticks)
                {
                    errors.Add($"Process {id} was created during the snapshot; defer attribution.");
                    continue;
                }
                var sample = new ProcessResourceSample(identity, row.ParentId, process.SessionId,
                    process.ProcessName, process.WorkingSet64, process.PrivateMemorySize64,
                    process.TotalProcessorTime.TotalSeconds);
                if (process.HasExited)
                {
                    errors.Add($"Process {id} exited during resource reading; sample discarded.");
                    continue;
                }
                samples.Add(sample);
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or ArgumentException)
            {
                errors.Add(exception is Win32Exception native
                    ? $"Process {id} observation failed: Win32 error {native.NativeErrorCode}."
                    : $"Process {id} observation failed: {exception.GetType().Name}.");
            }
        }
        return new(capturedUtc, topology.ContainsKey(root.Id), samples.ToArray(), errors.ToArray());
    }
}
