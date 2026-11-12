using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;

namespace Kora.Windows.Coordination;

public sealed partial class WindowsInstanceCoordinator
{
    private bool exitPending;

    public static async Task<InstanceExitResult> RequestExitAsync(CancellationToken cancellationToken)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var callerSid = identity.User?.Value ?? throw new InvalidOperationException("The user SID cannot be verified.");
        using var current = Process.GetCurrentProcess();
        var eligibilityPrompt = new NativeLifecyclePrompt(callerSid, current.SessionId);
        if (!eligibilityPrompt.IsEligible())
        {
            throw new InvalidOperationException("Exit requires an unlocked non-elevated interactive user session.");
        }
        using var self = VerifiedInstanceProcess.Open(current.Id, callerSid, current.SessionId);
        if (!HasAssistantOwner(callerSid)) { return new(InstanceExitStatus.NoOwner); }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(100));
        using var pipe = new NamedPipeClientStream(".", $"Kora.Instance.{callerSid}", PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(5000, timeout.Token).ConfigureAwait(false);
        if (!CoordinationNative.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var processId))
        {
            throw new InvalidOperationException("The Kora owner process cannot be authenticated.");
        }
        using var peer = VerifiedInstanceProcess.Open(processId, callerSid, current.SessionId);
        var request = new InstanceMessage(InstanceProtocol.Version, "Exit");
        InstanceExitPolicy.RequireRequest(request, peer.Identity, self.Identity);
        using var lifetime = Process.GetProcessById(processId);
        if (!CoordinationNative.GetProcessTimes(lifetime.SafeHandle, out var creation, out _, out _, out _)
            || creation != peer.Identity.CreationTime)
        {
            throw new InvalidOperationException("The exact Kora process lifetime cannot be retained.");
        }
        await InstanceShutdownHandshake.RequestStopAsync(pipe, lifetime.WaitForExitAsync, "Exit", timeout.Token)
            .ConfigureAwait(false);
        return new(InstanceExitStatus.Exited, processId, lifetime.ExitCode);
    }

    private static bool HasAssistantOwner(string sid)
    {
        using var mutex = CoordinationNative.CreateUserMutex($@"Global\Kora.AssistantOwner.{sid}", sid);
        if (!Acquire(mutex, TimeSpan.Zero, allowAbandoned: false)) { return true; }
        mutex.ReleaseMutex();
        return false;
    }

    private async Task HandleExitAsync(NamedPipeServerStream pipe, VerifiedInstanceProcess peer,
        InstanceMessage request, CancellationToken cancellationToken)
    {
        InstanceExitPolicy.RequireRequest(request, original.Identity, peer.Identity);
        Func<CancellationToken, Task<bool>> shutdown;
        lock (sync)
        {
            if (!ownsMutex || !callbacks.IsReady || applicationShutdown is null || pending is not null
                || restartRequested || installerShutdownPending || exitPending || !peer.IsAlive || !prompt.IsEligible())
            {
                throw new InvalidOperationException("The owner is busy, unavailable or outside current interactive ownership.");
            }
            shutdown = applicationShutdown;
            exitPending = true;
        }
        try
        {
            await ReplyAsync(pipe, "StopRequested", "", cancellationToken).ConfigureAwait(false);
            if (!await shutdown(cancellationToken).ConfigureAwait(false))
            {
                await ReplyAsync(pipe, "Busy", "Exit was cancelled or its input/work checks did not complete.",
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally { lock (sync) { exitPending = false; } }
    }
}
