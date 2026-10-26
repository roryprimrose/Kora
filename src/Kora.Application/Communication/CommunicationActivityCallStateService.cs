using Kora.Application.Configuration;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Communication;

public sealed partial class CommunicationActivityCallStateService : ICallStateService, IAsyncDisposable
{
    private readonly Lock gate = new();
    private readonly ICommunicationActivitySource source;
    private readonly LocalCommunicationActivityPreferences preferences;
    private readonly TimeProvider time;
    private readonly ILogger<CommunicationActivityCallStateService> logger;
    private readonly ITimer freshness;
    private Func<bool> eligible = static () => false;
    private Func<(long Owner, long Privacy)> hostRevision = static () => (0, 0);
    private sealed record ObservationEpoch(Guid Generation, long Owner, long Privacy);
    private volatile CommunicationActivitySelection selection;
    private CommunicationActivityObservation? observation;
    private CancellationTokenSource? cancellation;
    private Task work = Task.CompletedTask;
    private TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ObservationEpoch epoch = new(Guid.NewGuid(), 0, 0);
    private volatile CallState state = CallState.Unknown;
    private volatile bool preferencesAvailable;
    private volatile bool releaseFailed;
    private volatile bool sourceFailed;
    private bool disposed;
    private bool hostEligible;

    public CommunicationActivityCallStateService(ICommunicationActivitySource source,
        LocalCommunicationActivityPreferences preferences, TimeProvider time,
        ILogger<CommunicationActivityCallStateService> logger)
    {
        this.source = source;
        this.preferences = preferences;
        this.time = time;
        this.logger = logger;
        freshness = time.CreateTimer(_ => Refresh(), null, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));
    }

    public event EventHandler<CallStateChangedEventArgs>? StateChanged;
    public event EventHandler? Changed;
    public bool IsQualified => source.IsQualified;
    public CallState CurrentState => selection == CommunicationActivitySelection.None ? state : Normalize();
    public CommunicationActivitySelection Selection => selection;
    public bool PreferencesAvailable => preferencesAvailable;
    public bool ResourceReleaseFailed => releaseFailed;
    public CommunicationActivityObservation? Observation => Volatile.Read(ref observation);

    public void BindHost(Func<bool> hostEligible, Func<(long Owner, long Privacy)>? revision = null)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            eligible = hostEligible;
            hostRevision = revision ?? (static () => (0, 0));
            Restart();
        }
    }

    public void ObservePreferences()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            try
            {
                selection = preferences.Load();
                preferencesAvailable = true;
                Restart();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                HoldUnavailable();
                PreferenceFailed(logger);
                throw;
            }
        }
    }

    internal void HoldUnavailable()
    {
        lock (gate)
        {
            preferencesAvailable = false;
            Restart();
        }
    }

    internal Task ApplyConfirmed(CommunicationActivitySelection value)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            selection = value;
            preferencesAvailable = true;
            Restart();
#pragma warning disable VSTHRD003 // This completion is owned by the queued, UI-independent observation worker.
            return ready.Task;
#pragma warning restore VSTHRD003
        }
    }

    public void Refresh()
    {
        lock (gate)
        {
            if (disposed) { return; }
            var version = hostRevision();
            if (selection != CommunicationActivitySelection.None
                && (hostEligible != eligible() || epoch.Owner != version.Owner || epoch.Privacy != version.Privacy))
            { Restart(); }
            var normalized = Normalize();
            if (normalized != state) { Publish(normalized); }
        }
    }

    private CallState Normalize()
    {
        if (!preferencesAvailable || releaseFailed || sourceFailed) { return CallState.Unknown; }
        if (selection == CommunicationActivitySelection.None) { return work.IsCompleted ? CallState.Unavailable : CallState.Unknown; }
        var currentEpoch = Volatile.Read(ref epoch);
        var version = hostRevision();
        if (!source.IsQualified || !eligible() || observation is not { } value
            || currentEpoch.Owner != version.Owner || currentEpoch.Privacy != version.Privacy
            || value.Generation != currentEpoch.Generation || value.DeviceEpoch == Guid.Empty || value.Sequence < 1
            || value.Health != CommunicationActivityHealth.Healthy)
        { return CallState.Unknown; }
        var age = time.GetUtcNow() - value.ObservedAt;
        if (age < TimeSpan.Zero || age >= CommunicationActivityObservation.MaximumAge) { return CallState.Unknown; }
        return value.State is CallState.Clear or CallState.Suspected ? value.State : CallState.Unknown;
    }

    private void Restart()
    {
        var version = hostRevision();
        Volatile.Write(ref epoch, new(Guid.NewGuid(), version.Owner, version.Privacy));
        hostEligible = eligible();
        Volatile.Write(ref observation, null);
        ready.TrySetCanceled();
        ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var selectionReady = ready;
        cancellation?.Cancel();
        var previous = work;
        var oldCancellation = cancellation;
        cancellation = new();
        var token = cancellation.Token;
        var expectedGeneration = epoch.Generation;
        Publish(CallState.Unknown);
        var begin = HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Policy, RequestOrigin.HostSystem);
        work = Task.Run(async () =>
        {
            using var activity = begin();
            try
            {
#pragma warning disable VSTHRD003 // Serialize actual release of the prior owned worker; it never awaits UI dispatch.
                await previous.ConfigureAwait(false);
#pragma warning restore VSTHRD003
                oldCancellation?.Dispose();
                lock (gate)
                {
                    if (disposed || expectedGeneration != epoch.Generation || token.IsCancellationRequested)
                    { activity.Complete(HostOperationOutcome.Cancelled); return; }
                    if (!preferencesAvailable || releaseFailed)
                    { Publish(CallState.Unknown); activity.Complete(HostOperationOutcome.Failed); return; }
                    sourceFailed = false;
                    if (selection == CommunicationActivitySelection.None)
                    { Publish(CallState.Unavailable); activity.Complete(HostOperationOutcome.Completed); return; }
                    if (!source.IsQualified || !eligible())
                    { Publish(CallState.Unknown); activity.Complete(HostOperationOutcome.Failed); return; }
                }
                await source.ObserveAsync(expectedGeneration, value => Accept(expectedGeneration, value), token).ConfigureAwait(false);
                if (!token.IsCancellationRequested) { throw new InvalidOperationException("Communication observation ended unexpectedly."); }
                activity.Complete(HostOperationOutcome.Cancelled);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            { activity.Complete(HostOperationOutcome.Cancelled); }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                lock (gate)
                {
                    releaseFailed = source.HasUnreleasedResources;
                    sourceFailed = true;
                    Volatile.Write(ref observation, new(expectedGeneration, Guid.Empty, 0, CallState.Unknown,
                        time.GetUtcNow(), CommunicationActivityHealth.Failed));
                    Publish(CallState.Unknown);
                }
                ObservationFailed(logger);
                activity.Complete(HostOperationOutcome.Failed);
            }
            finally { selectionReady.TrySetResult(); }
        });
    }

    private void Accept(Guid expectedGeneration, CommunicationActivityObservation value)
    {
        lock (gate)
        {
            if (disposed || expectedGeneration != epoch.Generation || value.Generation != epoch.Generation
                || observation is { } previous && value.Sequence <= previous.Sequence)
            { return; }
            Volatile.Write(ref observation, value);
            Publish(Normalize());
            ready.TrySetResult();
        }
    }

    private void Publish(CallState value)
    {
        var changed = state != value;
        state = value;
        if (changed) { StateChanged?.Invoke(this, new(value)); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task JoinAsync()
    {
        Task pending;
        lock (gate) { pending = work; }
        await pending.ConfigureAwait(false);
        lock (gate)
        {
            if (releaseFailed) { throw new InvalidOperationException("Communication observation resource release is unconfirmed; source remains quarantined."); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task pending;
        lock (gate)
        {
            disposed = true;
            Volatile.Write(ref epoch, new(Guid.NewGuid(), 0, 0));
            cancellation?.Cancel();
            pending = work;
            Publish(CallState.Unknown);
        }
        await freshness.DisposeAsync().ConfigureAwait(false);
        await pending.ConfigureAwait(false);
        await source.DisposeAsync().ConfigureAwait(false);
        lock (gate)
        {
            releaseFailed = false;
            sourceFailed = false;
            cancellation?.Dispose();
            cancellation = null;
            StateChanged = null;
            Changed = null;
        }
    }
}