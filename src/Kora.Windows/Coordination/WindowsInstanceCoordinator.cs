using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;

using Kora.Core.Coordination;

namespace Kora.Windows.Coordination;

/// <summary>
/// Main-thread ownership surrounds the entire desktop/service lifetime. After a handoff,
/// that thread becomes a lifecycle-only supervisor; it never composes assistant services again.
/// </summary>
public sealed class WindowsInstanceCoordinator : IInstanceCoordinator
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(10);
    private readonly IInstanceHostCallbacks callbacks;
    private readonly string sid;
    private readonly int sessionId;
    private readonly string pipeName;
    private readonly Mutex owner;
    private readonly Mutex startupGate;
    private readonly Mutex returnBarrier;
    private readonly VerifiedInstanceProcess original;
    private readonly NativeLifecyclePrompt prompt;
    private readonly Guid epoch = Guid.NewGuid();
    private readonly CancellationTokenSource stopping = new();
    private readonly CancellationTokenSource listenerStopping = new();
    private readonly OwnerContinuity continuity;
    private readonly ConcurrentDictionary<NamedPipeServerStream, byte> connections = new();
    private readonly SemaphoreSlim connectionLimit = new(4, 4);
    private readonly Lock sync = new();
    private Task? server;
    private PendingHandoff? pending;
    private volatile bool ownsMutex;
    private bool ownsReturnBarrier;
    private bool disposed;
    private bool restartRequested;

    public WindowsInstanceCoordinator(IInstanceHostCallbacks callbacks)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Kora instance ownership requires Windows.");
        }

        this.callbacks = callbacks;
        using var identity = WindowsIdentity.GetCurrent();
        sid = identity.User?.Value ?? throw new InvalidOperationException("The user SID cannot be proven.");
        using var process = Process.GetCurrentProcess();
        sessionId = process.SessionId;
        if (sessionId == 0 || !Environment.UserInteractive)
        {
            throw new InvalidOperationException("A non-interactive process cannot own the assistant.");
        }

        // The ownership namespace is stable across protocol versions as well as builds.
        pipeName = $"Kora.Instance.{sid}";
        owner = CoordinationNative.CreateUserMutex($@"Global\Kora.AssistantOwner.{sid}", sid);
        startupGate = CoordinationNative.CreateUserMutex($@"Global\Kora.Lifecycle.{sid}", sid);
        returnBarrier = CoordinationNative.CreateUserMutex($@"Global\Kora.Return.{sid}", sid);
        original = VerifiedInstanceProcess.Open(Environment.ProcessId, sid, sessionId);
        prompt = new NativeLifecyclePrompt(sid, sessionId);
        continuity = new OwnerContinuity(sid);
    }

    public string? FailureReason { get; private set; }

    public InstanceBuildIdentity BuildIdentity => original.Identity.Build;

    public static void ReportStartupFailure(string message)
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            using var process = Process.GetCurrentProcess();
            if (identity.User is not null)
            {
                new NativeLifecyclePrompt(identity.User.Value, process.SessionId).ShowError(message);
            }
        }
        catch (Exception exception) when (IsLifecycleFailure(exception))
        {
            // No sensitive display or alternate startup when session identity is unavailable.
        }
    }

    public InstanceStartupDisposition Enter()
    {
        try
        {
            if (!Acquire(startupGate, StartupTimeout, allowAbandoned: false))
            {
                return Deny("Another lifecycle transition is pending. Try again after it finishes.");
            }

            try
            {
                if (Acquire(owner, TimeSpan.Zero, allowAbandoned: false))
                {
                    ownsMutex = true;
                    if (!prompt.IsEligible())
                    {
                        return Deny("Startup requires one authoritative unlocked interactive session. Unlock/connect this session or explicitly resolve multi-session ambiguity.");
                    }

                    continuity.Begin();
                    StartServer();
                    return InstanceStartupDisposition.Owner;
                }
            }
            finally
            {
                startupGate.ReleaseMutex();
            }

            return ContactExisting();
        }
        catch (Exception exception) when (IsLifecycleFailure(exception))
        {
            return Deny($"Ownership could not be verified: {exception.Message}");
        }
    }

    private InstanceStartupDisposition ContactExisting()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        pipe.Connect((int)StartupTimeout.TotalMilliseconds);
        if (!CoordinationNative.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var serverPid))
        {
            return Deny("The existing coordinator process could not be authenticated.");
        }

        using var existing = VerifiedInstanceProcess.Open(serverPid, sid, sessionId);
        if (existing.Identity.Build.IsSameBuild(original.Identity.Build))
        {
            timeout.CancelAfter(StartupTimeout);
        }
        Send(pipe, new InstanceMessage(InstanceProtocol.Version, "Start"), timeout.Token);
        var response = Receive(pipe, timeout.Token);
        if (string.Equals(response.Kind, "Activated", StringComparison.Ordinal))
        {
            return existing.Identity.Build.IsSameBuild(original.Identity.Build)
                ? InstanceStartupDisposition.ActivatedExisting
                : Deny("The existing instance returned an incompatible activation acknowledgement.");
        }

        if (!string.Equals(response.Kind, "Transfer", StringComparison.Ordinal) || existing.Identity.Build.IsSameBuild(original.Identity.Build) ||
            response.Epoch == Guid.Empty || response.Transaction == Guid.Empty || response.Ticket.Length != 64)
        {
            return Deny(string.IsNullOrEmpty(response.Detail) ? "The existing instance denied startup." : response.Detail);
        }

        if (!existing.IsAlive || !prompt.IsEligible())
        {
            return Deny("The approved handoff is no longer eligible.");
        }

        // Only this authenticated, still-live candidate bypasses the owner's startup reservation.
        if (!Acquire(owner, StartupTimeout, allowAbandoned: false))
        {
            return Deny("The original did not release exclusive ownership after quiescence.");
        }

        ownsMutex = true;
        continuity.Begin();
        Send(pipe, response with { Kind = "Commit", Detail = "" }, timeout.Token);
        var committed = Receive(pipe, timeout.Token);
        if (!string.Equals(committed.Kind, "Committed", StringComparison.Ordinal) || committed.Epoch != response.Epoch ||
            committed.Transaction != response.Transaction || !prompt.IsEligible())
        {
            ReleaseOwner(safelyDisposed: true);
            return Deny("The single-use handoff acknowledgement was not verified.");
        }

        StartServer();
        return InstanceStartupDisposition.Owner;
    }

    private InstanceStartupDisposition Deny(string reason)
    {
        FailureReason = reason;
        return InstanceStartupDisposition.Denied;
    }

    public void ShowFailure()
    {
        prompt.ShowError(FailureReason ?? "Kora could not start.");
    }

    private void StartServer()
    {
        var first = CreateServerPipe();
        server = Task.Run(() => ListenAsync(first, listenerStopping.Token), listenerStopping.Token);
    }

    private NamedPipeServerStream CreateServerPipe() =>
        new(pipeName, PipeDirection.InOut, 4, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 4096, 4096);

    private async Task ListenAsync(NamedPipeServerStream first, CancellationToken cancellationToken)
    {
        var pipe = first;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await connectionLimit.WaitAsync(cancellationToken).ConfigureAwait(false);
                connections.TryAdd(pipe, 0);
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                var connected = pipe;
                _ = HandleConnectionAsync(connected, stopping.Token);
                pipe = CreateServerPipe();
            }
        }
        catch (Exception exception) when (IsLifecycleFailure(exception))
        {
            FailureReason = "The ownership listener became unavailable; existing ownership is retained.";
        }
        finally
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        VerifiedInstanceProcess? peer = null;
        PendingHandoff? transaction = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(100));
        try
        {
            if (!CoordinationNative.GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var pid))
            {
                throw new InvalidOperationException("The caller process cannot be authenticated.");
            }

            peer = VerifiedInstanceProcess.Open(pid, sid, sessionId);
            var request = await InstanceProtocol.ReadAsync(pipe, timeout.Token).ConfigureAwait(false);
            if (string.Equals(request.Kind, "Ready", StringComparison.Ordinal))
            {
                await ReplyAsync(pipe, callbacks.IsReady && ownsMutex ? "Ready" : "Busy", "", timeout.Token)
                    .ConfigureAwait(false);
                return;
            }

            if (!string.Equals(request.Kind, "Start", StringComparison.Ordinal) || request.Epoch != Guid.Empty || request.Transaction != Guid.Empty ||
                request.Ticket.Length != 0)
            {
                throw new InvalidDataException("Unsupported or stale lifecycle request.");
            }

            if (!ownsMutex || !callbacks.IsReady)
            {
                await ReplyAsync(pipe, "Busy", "The owner is starting or stopping. No new assistant was started.", timeout.Token)
                    .ConfigureAwait(false);
                return;
            }

            if (original.Identity.Build.IsSameBuild(peer.Identity.Build))
            {
                if (prompt.IsEligible())
                {
                    using var activateTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                    activateTimeout.CancelAfter(StartupTimeout);
                    await callbacks.ActivateExistingAsync(activateTimeout.Token)
                        .WaitAsync(activateTimeout.Token).ConfigureAwait(false);
                }

                await ReplyAsync(pipe, "Activated", "", timeout.Token).ConfigureAwait(false);
                return;
            }

            lock (sync)
            {
                if (pending is null && !restartRequested)
                {
                    transaction = new PendingHandoff(peer, new HandoffTransaction(epoch, original.Identity,
                        peer.Identity, DateTimeOffset.UtcNow.AddSeconds(90)));
                    pending = transaction;
                    peer = null;
                }
            }

            if (transaction is null)
            {
                await ReplyAsync(pipe, "Busy", "One candidate or local lifecycle transition is already pending.", timeout.Token).ConfigureAwait(false);
                return;
            }

            if (!ReturnTransactionAvailable() || !prompt.IsEligible() ||
                !prompt.Ask(
                    $"{transaction.Peer.Identity.Build.DisplayName}\n\nwants to run instead of:\n\n" +
                    $"{original.Identity.Build.DisplayName}\n\n" +
                    "Allow that exact build to run after this instance safely stops?\n" +
                    "Yes requests a switch only when no task, setup, model or speech-install work is active. " +
                    "Active or uncertain work blocks the switch and is not silently cancelled; choose No and retry after it finishes. " +
                    "A permitted switch closes capture/playback and discards ephemeral approvals. " +
                    "This does not approve microphone consent, tasks, installation, or data migration.",
                    TimeSpan.FromSeconds(45), () => transaction.Peer.IsAlive && !timeout.IsCancellationRequested) ||
                !transaction.Transaction.Approve(DateTimeOffset.UtcNow, prompt.IsEligible(), transaction.Peer.IsAlive))
            {
                await ReplyAsync(pipe, "Declined", "The handoff was declined, locked, expired, or another return is pending.",
                    timeout.Token).ConfigureAwait(false);
                return;
            }

            transaction.QuiescenceRequested.TrySetResult(true);
            using var quiescenceTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            quiescenceTimeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var monitorStopping = new CancellationTokenSource();
            var monitor = MonitorEligibilityAsync(transaction.Peer, quiescenceTimeout, monitorStopping.Token);
            bool safe;
            try
            {
                quiescenceTimeout.Token.ThrowIfCancellationRequested();
                safe = await callbacks.QuiesceForHandoffAsync(quiescenceTimeout.Token)
                    .WaitAsync(quiescenceTimeout.Token).ConfigureAwait(false);
            }
            finally
            {
                await monitorStopping.CancelAsync().ConfigureAwait(false);
                await monitor.ConfigureAwait(false);
            }

            transaction.Quiesced.TrySetResult(safe);
            if (!safe)
            {
                await ReplyAsync(pipe, "Denied", "The original could not safely quiesce active work.", timeout.Token)
                    .ConfigureAwait(false);
                return;
            }

            await transaction.HostExited.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            var ticket = transaction.Transaction.IssueTicket(DateTimeOffset.UtcNow);
            if (ticket is null || !transaction.Peer.IsAlive || !prompt.IsEligible())
            {
                throw new InvalidOperationException("The quiescent transaction is no longer eligible.");
            }

            await InstanceProtocol.WriteAsync(pipe, new InstanceMessage(InstanceProtocol.Version, "Transfer",
                epoch, transaction.Transaction.Id, ticket), timeout.Token).ConfigureAwait(false);
            var commit = await InstanceProtocol.ReadAsync(pipe, timeout.Token).ConfigureAwait(false);
            if (!string.Equals(commit.Kind, "Commit", StringComparison.Ordinal) ||
                !transaction.Transaction.Commit(commit.Epoch, commit.Transaction, commit.Ticket,
                    transaction.Peer.Identity, DateTimeOffset.UtcNow, prompt.IsEligible() && transaction.Peer.IsAlive))
            {
                throw new InvalidOperationException("The process-bound single-use handoff ticket was rejected.");
            }

            await InstanceProtocol.WriteAsync(pipe, new InstanceMessage(InstanceProtocol.Version, "Committed",
                epoch, transaction.Transaction.Id), timeout.Token).ConfigureAwait(false);
            transaction.Committed.TrySetResult(true);
        }
        catch (Exception exception) when (IsLifecycleFailure(exception))
        {
            transaction?.Quiesced.TrySetResult(false);
            transaction?.Committed.TrySetResult(false);
            try
            {
                using var replyTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await ReplyAsync(pipe, "Denied", "Unproven identity, unsupported protocol, or failed lifecycle transition.",
                    replyTimeout.Token).ConfigureAwait(false);
            }
            catch (Exception replyException) when (IsLifecycleFailure(replyException))
            {
                // A lost client never permits ownership transfer.
            }
        }
        finally
        {
            if (transaction is not null)
            {
                transaction.QuiescenceRequested.TrySetResult(false);
                transaction.Quiesced.TrySetResult(false);
                transaction.Committed.TrySetResult(false);
                if (transaction.Transaction.Stage != HandoffStage.Transferred)
                {
                    transaction.Transaction.Cancel();
                }

                if (transaction.Transaction.Stage != HandoffStage.Transferred &&
                    !transaction.HostExited.Task.IsCompleted)
                {
                    lock (sync)
                    {
                        if (ReferenceEquals(pending, transaction))
                        {
                            pending = null;
                        }
                    }

                    transaction.Peer.Dispose();
                }
            }

            peer?.Dispose();
            connections.TryRemove(pipe, out _);
            await pipe.DisposeAsync().ConfigureAwait(false);
            connectionLimit.Release();
        }
    }

    private bool ReturnTransactionAvailable()
    {
        if (!Acquire(returnBarrier, TimeSpan.Zero, allowAbandoned: true))
        {
            return false;
        }

        returnBarrier.ReleaseMutex();
        return true;
    }

    private async Task MonitorEligibilityAsync(
        VerifiedInstanceProcess peer,
        CancellationTokenSource quiescence,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!peer.IsAlive || !prompt.IsEligible())
                {
                    await quiescence.CancelAsync().ConfigureAwait(false);
                    return;
                }

                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A completed/refused quiescence no longer needs observation.
        }
    }

#pragma warning disable VSTHRD002 // The desktop dispatcher has exited; this is the STA ownership thread.
    /// <summary>Call on the Enter thread, only after the desktop AND all services are disposed.</summary>
    public void CompleteHostExit(bool safelyDisposed)
    {
        PendingHandoff? transaction;
        lock (sync)
        {
            transaction = pending;
        }

        if (transaction is null && restartRequested && safelyDisposed)
        {
            StopServer();
            ReleaseOwner(safelyDisposed: true);
            try
            {
                RestartExactOriginal();
            }
            catch (Exception exception) when (IsLifecycleFailure(exception))
            {
                prompt.ShowError($"The requested restart failed: {exception.Message}\n" +
                    "No alternate executable was launched. Start Kora manually after resolving ownership.");
            }

            return;
        }

        if (transaction is null || !safelyDisposed ||
            !transaction.QuiescenceRequested.Task.IsCompletedSuccessfully ||
            !transaction.QuiescenceRequested.Task.Result)
        {
            ReleaseOwner(safelyDisposed);
            return;
        }

        var safe = WaitForResult(transaction.Quiesced.Task, TimeSpan.FromSeconds(5));
        if (!safe || !transaction.Peer.IsAlive || !prompt.IsEligible() ||
            !Acquire(startupGate, StartupTimeout, allowAbandoned: false))
        {
            transaction.Transaction.Cancel();
            ReleaseOwner(safelyDisposed);
            return;
        }

        try
        {
            if (!Acquire(returnBarrier, TimeSpan.Zero, allowAbandoned: true))
            {
                transaction.Transaction.Cancel();
                ReleaseOwner(safelyDisposed);
                return;
            }

            ownsReturnBarrier = true;
            if (!transaction.Transaction.MarkQuiescent(DateTimeOffset.UtcNow, safelyDisposed, transaction.Peer.IsAlive))
            {
                ReleaseOwner(safelyDisposed);
                return;
            }

            StopAccepting();
            ReleaseOwner(safelyDisposed: true);
            transaction.HostExited.TrySetResult(true);
            _ = WaitForResult(transaction.Committed.Task, TimeSpan.FromSeconds(15));
        }
        finally
        {
            startupGate.ReleaseMutex();
        }

        // No assistant services, mutable-store handles, logging, model, or capture exist past this boundary.
        StopServer();
        SuperviseReturn(transaction);
    }
#pragma warning restore VSTHRD002

    private void SuperviseReturn(PendingHandoff transaction)
    {
        try
        {
            if (!WaitForReplacementReadiness(transaction.Peer))
            {
                prompt.ShowError("The replacement did not acknowledge normal desktop readiness. Only its actual process lifetime is monitored; no automatic recovery is promised.");
            }

            transaction.Peer.WaitForExit();
            while (!prompt.IsEligible())
            {
                Thread.Sleep(1000);
                using var current = Process.GetCurrentProcess();
                if (current.SessionId != sessionId)
                {
                    return;
                }
            }

            if (!OwnerAvailable())
            {
                prompt.ShowError("The replacement exited, but another assistant or uncertain owner remains. Launch the original manually after resolving ownership.");
                return;
            }

            if (!prompt.Ask(
                $"{transaction.Peer.Identity.Build.DisplayName}\n\nhas exited.\n\n" +
                $"Start the exact original again?\n\n{original.Identity.Build.DisplayName}\n\n" +
                "No work or grants are resumed. The original uses its own saved consent and startup gates.",
                TimeSpan.FromMinutes(2), OwnerAvailable))
            {
                return;
            }

            RestartExactOriginal();
        }
        catch (Exception exception) when (IsLifecycleFailure(exception))
        {
            prompt.ShowError($"The return offer failed: {exception.Message}\n" +
                "No automatic restart occurred. Use the normal original launch after resolving ownership.");
        }
        finally
        {
            transaction.Peer.Dispose();
            if (ownsReturnBarrier)
            {
                returnBarrier.ReleaseMutex();
                ownsReturnBarrier = false;
            }
        }
    }

    private void RestartExactOriginal()
    {
        if (!prompt.IsEligible() || !Acquire(startupGate, StartupTimeout, allowAbandoned: false))
        {
            throw new InvalidOperationException("Another lifecycle transition is active.");
        }

        Process? restarted = null;
        try
        {
            if (!OwnerAvailable())
            {
                throw new InvalidOperationException("Another assistant became active.");
            }

            var files = new List<FileStream>();
            try
            {
                var current = VerifiedInstanceProcess.ReadBuild(original.Identity.Build.ExecutablePath, files);
                if (current != original.Identity.Build || !prompt.IsEligible())
                {
                    throw new InvalidOperationException("The approved original executable identity changed.");
                }

                restarted = Process.Start(new ProcessStartInfo
                {
                    FileName = current.ExecutablePath,
                    WorkingDirectory = current.DeploymentIdentity,
                    UseShellExecute = false,
                }) ?? throw new InvalidOperationException("The exact original could not start.");
            }
            finally
            {
                foreach (var file in files)
                {
                    file.Dispose();
                }
            }
        }
        finally
        {
            startupGate.ReleaseMutex();
        }

        using (restarted)
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (DateTimeOffset.UtcNow < deadline && !restarted.HasExited)
            {
                try
                {
                    using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    pipe.Connect(500);
                    if (!CoordinationNative.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var pid) ||
                        pid != restarted.Id)
                    {
                        throw new InvalidOperationException("A different assistant owns the restart.");
                    }

                    using var verified = VerifiedInstanceProcess.Open(pid, sid, sessionId);
                    if (verified.Identity.Build != original.Identity.Build)
                    {
                        throw new InvalidOperationException("The restarted original identity changed.");
                    }

                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    Send(pipe, new InstanceMessage(InstanceProtocol.Version, "Ready"), timeout.Token);
                    if (string.Equals(Receive(pipe, timeout.Token).Kind, "Ready", StringComparison.Ordinal))
                    {
                        return;
                    }
                }
                catch (TimeoutException)
                {
                    // Readiness is bounded; the supervisor never relaunches or kills a failed original.
                }

                Thread.Sleep(250);
            }
        }

        throw new InvalidOperationException("The original did not acknowledge normal startup readiness.");
    }

    private bool WaitForReplacementReadiness(VerifiedInstanceProcess replacement)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline && replacement.IsAlive)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                pipe.Connect(500);
                if (!CoordinationNative.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var pid) ||
                    pid != replacement.Identity.ProcessId)
                {
                    return false;
                }

                using var verified = VerifiedInstanceProcess.Open(pid, sid, sessionId);
                if (verified.Identity != replacement.Identity)
                {
                    return false;
                }

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                Send(pipe, new InstanceMessage(InstanceProtocol.Version, "Ready"), timeout.Token);
                if (string.Equals(Receive(pipe, timeout.Token).Kind, "Ready", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            catch (Exception exception) when (IsLifecycleFailure(exception))
            {
                // Retain lifetime monitoring, but never mistake a failed/unreachable startup for readiness.
            }

            Thread.Sleep(250);
        }

        return false;
    }

    private bool OwnerAvailable()
    {
        if (!Acquire(owner, TimeSpan.Zero, allowAbandoned: false))
        {
            return false;
        }

        try
        {
            return !continuity.HasUncleanOwner;
        }
        finally
        {
            owner.ReleaseMutex();
        }
    }

    public void RequestRestartAfterQuiescence()
    {
        lock (sync)
        {
            if (!ownsMutex || pending is not null || restartRequested || !prompt.IsEligible())
            {
                throw new InvalidOperationException("Another lifecycle transition is pending or the session is ineligible.");
            }

            restartRequested = true;
        }
    }

    private static bool Acquire(Mutex mutex, TimeSpan timeout, bool allowAbandoned)
    {
        try
        {
            return mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException exception)
        {
            // Even a dead host is not evidence of quiescent workers/remote effects.
            if (!allowAbandoned)
            {
                mutex.ReleaseMutex();
                throw new InvalidOperationException("The previous owner died without verified quiescence. Resolve uncertain work before trying again.", exception);
            }

            return true;
        }
    }

#pragma warning disable VSTHRD002 // Bounded wait only after the desktop dispatcher exits.
    private static bool WaitForResult(Task<bool> task, TimeSpan timeout) =>
        task.Wait(timeout) && task.IsCompletedSuccessfully && task.Result;
#pragma warning restore VSTHRD002

    private void ReleaseOwner(bool safelyDisposed = false)
    {
        if (ownsMutex)
        {
            continuity.End(safelyDisposed);
            owner.ReleaseMutex();
            ownsMutex = false;
        }
    }

    private static Task ReplyAsync(Stream stream, string kind, string detail, CancellationToken cancellationToken) =>
        InstanceProtocol.WriteAsync(stream, new InstanceMessage(InstanceProtocol.Version, kind, Detail: detail), cancellationToken);

#pragma warning disable VSTHRD002 // STA executable startup/lifecycle boundary; never called from the desktop dispatcher.
    private static void Send(Stream stream, InstanceMessage message, CancellationToken cancellationToken) =>
        InstanceProtocol.WriteAsync(stream, message, cancellationToken).GetAwaiter().GetResult();

    private static InstanceMessage Receive(Stream stream, CancellationToken cancellationToken) =>
        InstanceProtocol.ReadAsync(stream, cancellationToken).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002

    private static bool IsLifecycleFailure(Exception exception) =>
        exception is IOException or InvalidOperationException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception or OperationCanceledException or TimeoutException or
            System.Text.Json.JsonException or ArgumentException or BadImageFormatException;

    private void StopServer()
    {
        StopAccepting();
        stopping.Cancel();
        foreach (var connection in connections.Keys)
        {
            connection.Dispose();
        }

    }

#pragma warning disable VSTHRD002 // Startup/disposal thread joins only the IPC listener, never desktop work.
    private void StopAccepting()
    {
        listenerStopping.Cancel();
        if (server is not null)
        {
            try
            {
                if (!server.Wait(TimeSpan.FromSeconds(2)))
                {
                    throw new InvalidOperationException("The original ownership listener did not actually stop; transfer is blocked.");
                }
            }
#pragma warning restore VSTHRD002
            catch (AggregateException)
            {
                // Shutdown does not imply any permission to restart or transfer.
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        StopServer();
        ReleaseOwner();
        if (ownsReturnBarrier)
        {
            returnBarrier.ReleaseMutex();
        }

        original.Dispose();
        continuity.Dispose();
        owner.Dispose();
        startupGate.Dispose();
        returnBarrier.Dispose();
        stopping.Dispose();
        listenerStopping.Dispose();
        // Connection handlers may still be completing after bounded listener shutdown.
        GC.SuppressFinalize(this);
    }

    private sealed class PendingHandoff(VerifiedInstanceProcess peer, HandoffTransaction transaction)
    {
        internal VerifiedInstanceProcess Peer { get; } = peer;
        internal HandoffTransaction Transaction { get; } = transaction;
        internal TaskCompletionSource<bool> QuiescenceRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> Quiesced { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> HostExited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> Committed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
