using System.Text;
using Kora.Core.Context;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Microsoft.Extensions.Logging;

namespace Kora.Tools.Clipboard;

public sealed partial class ClipboardSnapshotBroker(
    IPlainTextClipboardReader reader, TimeProvider time, ILogger<ClipboardSnapshotBroker> logger) : IDisposable, IAsyncDisposable
{
    private readonly Lock sync = new();
    private readonly Guid sourceId = Guid.NewGuid();
    private CancellationTokenSource? pending;
    private TaskCompletionSource? quiescence;
    private ClipboardSnapshot? snapshot;
    private Func<bool>? eligible;
    private long generation;
    private bool disposed;
    private bool resourceReleaseFailed;

    public event EventHandler? Changed;
    public bool IsReading { get { lock (sync) { return pending is not null; } } }
    public bool IsQuiescent { get { lock (sync) { return pending is null && !resourceReleaseFailed; } } }
    public async Task WaitForQuiescenceAsync()
    {
        Task outstanding;
        lock (sync) { outstanding = quiescence?.Task ?? Task.CompletedTask; }
        await outstanding.ConfigureAwait(false);
        lock (sync)
        {
            if (resourceReleaseFailed)
            {
                throw new InvalidOperationException(
                    "Native clipboard resource release was not verified. No clean ownership release is claimed; restart is required.");
            }
        }
    }

    public ClipboardSnapshot? Current
    {
        get
        {
            bool close;
            lock (sync) { close = eligible is not null && !eligible(); }
            if (close) { Clear(); }
            lock (sync) { return snapshot; }
        }
    }

    public async Task<ClipboardOutcome> CaptureAsync(Func<bool> canPresent, CancellationToken cancellationToken)
    {
        var request = HostActivity.RequireCurrent().Request;
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Tool);
        CancellationTokenSource cancellation;
        TaskCompletionSource admittedQuiescence;
        long admittedGeneration;
        lock (sync)
        {
            if (disposed || !ClipboardCommand.IsDeliberateOrigin(request.Origin) || !canPresent())
            {
                activity.Complete(HostOperationOutcome.Failed);
                Report(logger, ClipboardOutcome.Denied);
                return ClipboardOutcome.Denied;
            }
            if (pending is not null)
            {
                activity.Complete(HostOperationOutcome.Failed);
                Report(logger, ClipboardOutcome.Busy);
                return ClipboardOutcome.Busy;
            }
            if (resourceReleaseFailed)
            {
                activity.Complete(HostOperationOutcome.Failed);
                Report(logger, ClipboardOutcome.Unavailable);
                return ClipboardOutcome.Unavailable;
            }
            snapshot = null;
            eligible = canPresent;
            admittedGeneration = ++generation;
            pending = cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            quiescence = admittedQuiescence = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        ClipboardOutcome outcome;
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
            cancellation.Token.ThrowIfCancellationRequested();
            var read = await reader.ReadAsync(cancellation.Token).ConfigureAwait(false);
            lock (sync)
            {
                if (!read.ResourcesReleased)
                {
                    resourceReleaseFailed = true;
                    ReadFailed(logger, "ResourceReleaseUnverified");
                    outcome = ClipboardOutcome.Unavailable;
                }
                else if (disposed || admittedGeneration != generation || cancellation.IsCancellationRequested || !canPresent())
                {
                    outcome = ClipboardOutcome.Cancelled;
                }
                else
                {
                    outcome = Admit(read, request);
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            outcome = ClipboardOutcome.Cancelled;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            ReadFailed(logger, exception.GetType().Name);
            outcome = ClipboardOutcome.Unavailable;
        }
        finally
        {
            lock (sync) { pending = null; }
            cancellation.Dispose();
            admittedQuiescence.SetResult();
        }
        Report(logger, outcome);
        activity.Complete(outcome == ClipboardOutcome.Captured ? HostOperationOutcome.Completed
            : outcome == ClipboardOutcome.Cancelled ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        Changed?.Invoke(this, EventArgs.Empty);
        return outcome;
    }

    private ClipboardOutcome Admit(ClipboardReadResult read, HostRequest request)
    {
        if (read.Outcome != ClipboardOutcome.Captured) { return read.Outcome; }
        if (read.Text is null || read.Version == 0) { return ClipboardOutcome.Unavailable; }
        if (read.Text.Length == 0) { return ClipboardOutcome.Empty; }
        try
        {
            snapshot = new(sourceId, Guid.NewGuid(), request, read.Text, read.Version, time.GetUtcNow());
            return ClipboardOutcome.Captured;
        }
        catch (EncoderFallbackException) { return ClipboardOutcome.InvalidText; }
        catch (InvalidDataException)
        {
            return read.Text.Contains('\0', StringComparison.Ordinal) ? ClipboardOutcome.InvalidText : ClipboardOutcome.Oversize;
        }
    }

    public ClipboardOutcome Reuse(Guid snapshotId, Func<bool> canPresent)
    {
        var request = HostActivity.RequireCurrent().Request;
        var current = Current;
        ClipboardOutcome outcome;
        lock (sync)
        {
            outcome = disposed || !ClipboardCommand.IsDeliberateOrigin(request.Origin) || !canPresent()
                ? ClipboardOutcome.Denied
                : current is null || current.SnapshotId != snapshotId || !ReferenceEquals(current, snapshot)
                    ? ClipboardOutcome.Stale : ClipboardOutcome.Reused;
        }
        Report(logger, outcome);
        if (outcome == ClipboardOutcome.Reused) { Changed?.Invoke(this, EventArgs.Empty); }
        return outcome;
    }

    public void Clear()
    {
        lock (sync)
        {
            ++generation;
            snapshot = null;
            eligible = null;
            pending?.Cancel();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public ClipboardOutcome Revoke(Func<bool> canPresent)
    {
        var request = HostActivity.RequireCurrent().Request;
        var outcome = !ClipboardCommand.IsDeliberateOrigin(request.Origin) || !canPresent()
            ? ClipboardOutcome.Denied : ClipboardOutcome.Revoked;
        if (outcome == ClipboardOutcome.Revoked) { Clear(); }
        Report(logger, outcome);
        return outcome;
    }

    public void Dispose()
    {
        lock (sync) { disposed = true; }
        Clear();
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        await WaitForQuiescenceAsync().ConfigureAwait(false);
    }
}
