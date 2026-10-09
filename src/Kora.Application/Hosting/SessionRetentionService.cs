using Kora.Application.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Hosting;

public sealed partial class SessionRetentionService(
    ISessionRetentionStore store, SessionRetentionConfigurationService configuration,
    ISessionWorkspaceAccess access, IUiDispatcher dispatcher, TimeProvider time, ILogger<SessionRetentionService> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim serial = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private PeriodicTimer? timer;
    private Task? periodic;
    private readonly Lock disposalGate = new();
    private Task? disposal;
    private bool disposed;
    public event Action<HostId<SessionIdentity>>? Revoking;
    public event Action<string>? Failed;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (periodic is not null) { throw new InvalidOperationException("Session retention is already started."); }
        timer = new(TimeSpan.FromMinutes(1), time);
        periodic = RunPeriodicAsync(timer);
    }

    private async Task RunPeriodicAsync(PeriodicTimer ticks)
    {
        try
        {
            while (true)
            {
                await ticks.WaitForNextTickAsync(lifetime.Token).ConfigureAwait(false);
                // This exclusively owned timer is disposed only after lifetime cancellation.
                lifetime.Token.ThrowIfCancellationRequested();
                if (!configuration.Available || !access.CanControl) { Held(logger); continue; }
                try { await RunAsync(lifetime.Token).ConfigureAwait(false); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
                {
                    await dispatcher.InvokeAsync(() =>
                    {
                        Failed?.Invoke("Session retention held: " + exception.Message);
                        return Task.CompletedTask;
                    }).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        lock (disposalGate) { return new(disposal ??= CloseAsync()); }
    }

    private async Task CloseAsync()
    {
        disposed = true;
        await lifetime.CancelAsync().ConfigureAwait(false);
        timer?.Dispose();
#pragma warning disable VSTHRD003 // Join the owned, cancelled timer loop; it never captures a UI synchronization context.
        if (periodic is not null) { await periodic.ConfigureAwait(false); }
#pragma warning restore VSTHRD003
        await serial.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        serial.Release();
        serial.Dispose();
        lifetime.Dispose();
        Revoking = null;
        Failed = null;
    }

    public async Task<SessionRetentionBatch> RunAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        token = linked.Token;
        await serial.WaitAsync(token).ConfigureAwait(false);
        try { return await RunSerializedAsync(token).ConfigureAwait(false); }
        finally { serial.Release(); }
    }

    private async Task<SessionRetentionBatch> RunSerializedAsync(CancellationToken token)
    {
        var revision = access.ControlRevision;
        bool Eligible() => configuration.Available && access.CanControl && access.ControlRevision == revision;
        if (!Eligible())
        {
            throw new InvalidOperationException("Session retention is held by unavailable preferences or host ownership/privacy admission.");
        }
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
            HostActivityLayer.Application, HostOperation.Retention);
        try
        {
            var result = await store.ApplyRetentionAsync(Eligible, async (id, cancellation) =>
            {
                cancellation.ThrowIfCancellationRequested();
                await dispatcher.InvokeAsync(() =>
                {
                    Revoking?.Invoke(id);
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            Completed(logger, result.Archived, result.Deleted, result.Held, result.HasMore);
            host.Complete(HostOperationOutcome.Completed);
            return result;
        }
        catch (OperationCanceledException)
        {
            host.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch (Exception exception)
        {
            Failure(logger, exception.GetType().Name);
            host.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }
}
