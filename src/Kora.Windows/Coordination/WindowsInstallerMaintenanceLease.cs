using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;

namespace Kora.Windows.Coordination;

/// <summary>Reserves one user's existing lifecycle namespace until native package work has finished.</summary>
public sealed class WindowsInstallerMaintenanceLease : IInstallerMaintenanceLease
{
    private readonly Lock sync = new();
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private InstallerMaintenanceContinuity? continuity;
    private bool releaseRequested;

    private WindowsInstallerMaintenanceLease() { }

    public static void ValidateUserSession(bool allUsers)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value ?? throw new InvalidOperationException("The installer user SID is unavailable.");
        using var current = Process.GetCurrentProcess();
        using var self = CoordinationNative.OpenProcess(CoordinationNative.QueryLimitedInformation, false, current.Id);
        _ = CoordinationNative.ReadProcessToken(self);
        var prompt = new NativeLifecyclePrompt(sid, current.SessionId);
        if (!prompt.IsEligible() || (allUsers && prompt.HasOtherUserSessions()))
        {
            throw new InvalidOperationException("The installer session is locked, ambiguous, elevated or another user is logged on.");
        }
    }

    public static async Task<WindowsInstallerMaintenanceLease> AcquireAsync(bool allUsers, CancellationToken cancellationToken)
    {
        var lease = new WindowsInstallerMaintenanceLease();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Named mutex ownership is thread-affine; keep the reservation on one dedicated lifecycle thread.
        var thread = new Thread(() => lease.Reserve(ready, allUsers, cancellationToken))
        {
            IsBackground = true,
            Name = "Kora installer lifecycle reservation",
        };
        thread.Start();
        try
        {
            await ready.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return lease;
        }
        catch
        {
            await lease.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private void Reserve(TaskCompletionSource ready, bool allUsers, CancellationToken cancellationToken)
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var sid = identity.User?.Value ?? throw new InvalidOperationException("The installer user SID is unavailable.");
            using var current = Process.GetCurrentProcess();
            using var self = CoordinationNative.OpenProcess(CoordinationNative.QueryLimitedInformation, false, current.Id);
            _ = CoordinationNative.ReadProcessToken(self);
            var prompt = new NativeLifecyclePrompt(sid, current.SessionId);
            if (!prompt.IsEligible() || (allUsers && prompt.HasOtherUserSessions()))
            {
                throw new InvalidOperationException("Setup requires one unlocked non-elevated interactive user session.");
            }

            using var lifecycle = CoordinationNative.CreateUserMutex($@"Global\Kora.Lifecycle.{sid}", sid);
            using var returning = CoordinationNative.CreateUserMutex($@"Global\Kora.Return.{sid}", sid);
            using var owner = CoordinationNative.CreateUserMutex($@"Global\Kora.AssistantOwner.{sid}", sid);
            Acquire(lifecycle);
            try
            {
                Acquire(returning);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!TryAcquire(owner))
                    {
#pragma warning disable VSTHRD002 // Dedicated thread retains thread-affine mutexes; no UI dispatcher is blocked.
                        StopOwnerAsync(sid, current.SessionId, cancellationToken).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
                        Acquire(owner);
                    }
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var marker = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "Kora", "Coordination", "unclean-owner");
                        if (HasUncleanOwner(marker) || HasUncleanOwner(marker + ".debug-recovery") ||
                            !prompt.IsEligible() || (allUsers && prompt.HasOtherUserSessions()))
                        {
                            throw new InvalidOperationException("Kora has no verified clean shutdown or the user session changed. No package changes were admitted.");
                        }
                        using var reservationContinuity = new InstallerMaintenanceContinuity(
                            new WindowsInstallerMaintenanceMarkerStore(marker, sid));
                        lock (sync)
                        {
                            continuity = reservationContinuity;
                            if (releaseRequested) { reservationContinuity.SealForDisposal(); }
                        }
                        cancellationToken.ThrowIfCancellationRequested();
                        ready.TrySetResult();
#pragma warning disable VSTHRD002 // Release is signalled by setup only after package work/rollback has terminated.
                        release.Task.GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
                    }
                    finally { owner.ReleaseMutex(); }
                }
                finally { returning.ReleaseMutex(); }
            }
            finally { lifecycle.ReleaseMutex(); }
            finished.TrySetResult();
        }
        catch (Exception exception)
        {
            if (ready.TrySetException(exception)) { finished.TrySetResult(); }
            else { finished.TrySetException(exception); }
        }
    }

    private static void Acquire(Mutex mutex)
    {
        if (!TryAcquire(mutex))
        {
            throw new InvalidOperationException("Another Kora lifecycle operation is active. No package changes were admitted.");
        }
    }

    private static bool HasUncleanOwner(string path)
    {
        try { _ = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static bool TryAcquire(Mutex mutex)
    {
        try { return mutex.WaitOne(TimeSpan.Zero); }
        catch (AbandonedMutexException exception)
        {
            mutex.ReleaseMutex();
            throw new InvalidOperationException("A previous lifecycle owner exited without confirmed quiescence. Resolve it before retrying setup.", exception);
        }
    }

    private static async Task StopOwnerAsync(string sid, int sessionId, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(120));
        using var pipe = new NamedPipeClientStream(".", $"Kora.Instance.{sid}", PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(5000, timeout.Token).ConfigureAwait(false);
        if (!CoordinationNative.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var processId))
        {
            throw new InvalidOperationException("The Kora owner process cannot be authenticated.");
        }
        using var peer = VerifiedInstanceProcess.Open(processId, sid, sessionId);
        using var lifetime = Process.GetProcessById(processId);
        if (!CoordinationNative.GetProcessTimes(lifetime.SafeHandle, out var creation, out _, out _, out _) ||
            creation != peer.Identity.CreationTime)
        {
            throw new InvalidOperationException("The exact owner process lifetime cannot be retained.");
        }
        await InstallerShutdownHandshake.RequestStopAsync(pipe, lifetime.WaitForExitAsync, timeout.Token)
            .ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        lock (sync)
        {
            releaseRequested = true;
            continuity?.SealForDisposal();
            release.TrySetResult();
        }
#pragma warning disable VSTHRD003 // Joins the owned reservation thread's completion signal; it never requires the UI context.
        await finished.Task.ConfigureAwait(false);
#pragma warning restore VSTHRD003
    }

    public void BeginNativeApply()
    {
        lock (sync)
        {
            if (releaseRequested || continuity is null)
            {
                throw new InvalidOperationException("The durable maintenance reservation is unavailable.");
            }
            continuity.BeginNativeApply();
        }
    }

    public bool ConfirmNativeApplyComplete()
    {
        lock (sync) { return !releaseRequested && continuity?.ConfirmNativeApplyComplete() == true; }
    }
}
