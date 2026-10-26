using System.Threading.Channels;

using Kora.Core.Communication;
using Kora.Core.Diagnostics;

using Microsoft.Extensions.Logging;

namespace Kora.Windows.Communication;

public sealed partial class WindowsCommunicationsActivitySource : ICommunicationActivitySource
{
    private readonly WindowsCommunicationsObserver observer;

    public WindowsCommunicationsActivitySource(TimeProvider time, ILogger<WindowsCommunicationsActivitySource> logger)
    {
        observer = new(new WindowsCommunicationsNotificationPlatform(), time, logger);
    }

    // No installed/native profile evidence is bundled. Registration is not qualification.
    public bool IsQualified => false;
    public bool HasUnreleasedResources => observer.HasUnreleasedResources;

    public Task ObserveAsync(Guid generation, Action<CommunicationActivityObservation> publish, CancellationToken token)
    {
        if (!IsQualified) { throw new InvalidOperationException("The Windows communications notification profile is not qualified and cannot be enabled."); }
        return observer.RunAsync(generation, publish, token);
    }

    public ValueTask DisposeAsync() => observer.DisposeAsync();
    internal enum WindowsCommunicationSignalKind { Started, Ended, Invalid }
    internal sealed record WindowsCommunicationSignal(WindowsCommunicationSignalKind Kind, string? SessionId, uint Count);
    internal enum WindowsCommunicationIdentity { Unknown, Kora, Other }

    internal interface IWindowsCommunicationsNotificationLease : IDisposable
    {
        Guid DeviceEpoch { get; }
        CommunicationActivityHealth ReadHealth();
        WindowsCommunicationIdentity Resolve(string sessionId);
    }

    internal interface IWindowsCommunicationsNotificationPlatform
    {
        IWindowsCommunicationsNotificationLease Open(Action<WindowsCommunicationSignal> publish);
    }

    internal sealed partial class WindowsCommunicationsObserver(
        IWindowsCommunicationsNotificationPlatform platform, TimeProvider time,
        ILogger<WindowsCommunicationsActivitySource> logger) : IAsyncDisposable
    {
        private readonly Lock gate = new();
        private IWindowsCommunicationsNotificationLease? owned;
        private bool running;
        private bool disposed;
        internal bool HasUnreleasedResources { get { lock (gate) { return owned is not null; } } }

        internal async Task RunAsync(Guid generation, Action<CommunicationActivityObservation> publish, CancellationToken token)
        {
            if (generation == Guid.Empty) { throw new ArgumentException("An observation generation is required.", nameof(generation)); }
            var channel = Channel.CreateBounded<WindowsCommunicationSignal>(new BoundedChannelOptions(32)
            { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
            var overflow = 0;
            var accepts = 1;
            long sequence = 0;
            CallState state = CallState.Unknown;
            string? singleSession = null;
            Exception? failure = null;
            void Receive(WindowsCommunicationSignal signal)
            {
                if (Volatile.Read(ref accepts) == 0) { return; }
                if (string.IsNullOrEmpty(signal.SessionId) || signal.SessionId.Length > 1024)
                { signal = new(WindowsCommunicationSignalKind.Invalid, null, 0); }
                if (!channel.Writer.TryWrite(signal)) { Interlocked.Exchange(ref overflow, 1); }
            }
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (running || owned is not null) { throw new InvalidOperationException("A communications subscription is already owned or quarantined."); }
                running = true;
            }
            using var activity = HostActivity.BeginOperation(HostActivityLayer.Windows, HostOperation.Policy);
            try
            {
                token.ThrowIfCancellationRequested();
                var lease = platform.Open(Receive);
                lock (gate) { owned = lease; }
                void Publish(CommunicationActivityHealth health) => publish(new(generation, lease.DeviceEpoch,
                    checked(++sequence), health == CommunicationActivityHealth.Healthy ? state : CallState.Unknown,
                    time.GetUtcNow(), health));
                Publish(CommunicationActivityHealth.Starting);
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), time);
                var tick = timer.WaitForNextTickAsync(token).AsTask();
                var available = channel.Reader.WaitToReadAsync(token).AsTask();
                while (true)
                {
                    await Task.WhenAny(tick, available).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (Interlocked.Exchange(ref overflow, 0) != 0)
                    { throw new InvalidDataException("Communications notification capacity was exceeded; the epoch cannot be reconciled."); }
                    while (channel.Reader.TryRead(out var signal))
                    {
                        var sessionId = signal.SessionId;
                        if (string.IsNullOrEmpty(sessionId) || signal.Kind == WindowsCommunicationSignalKind.Invalid)
                        { state = CallState.Unknown; singleSession = null; }
                        else if (signal.Kind == WindowsCommunicationSignalKind.Started)
                        {
                            var identity = lease.Resolve(sessionId);
                            state = signal.Count is > 0 and <= 128 && identity == WindowsCommunicationIdentity.Other
                                ? CallState.Suspected : CallState.Unknown;
                            singleSession = state == CallState.Suspected && signal.Count == 1 ? sessionId : null;
                        }
                        else if (signal.Kind == WindowsCommunicationSignalKind.Ended)
                        {
                            // Only a witnessed, single non-Kora stream ending establishes this scope's baseline.
                            state = singleSession is not null && string.Equals(singleSession, sessionId, StringComparison.Ordinal)
                                ? CallState.Clear : CallState.Unknown;
                            singleSession = null;
                        }
                        else { state = CallState.Unknown; singleSession = null; }
                    }
                    var health = lease.ReadHealth();
                    if (health != CommunicationActivityHealth.Healthy) { state = CallState.Unknown; singleSession = null; }
                    Publish(health);
                    if (health != CommunicationActivityHealth.Healthy)
                    { throw new InvalidOperationException("The communications source device or Windows session scope was lost."); }
                    if (tick.IsCompleted) { await tick.ConfigureAwait(false); tick = timer.WaitForNextTickAsync(token).AsTask(); }
                    if (available.IsCompleted) { await available.ConfigureAwait(false); available = channel.Reader.WaitToReadAsync(token).AsTask(); }
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException) { failure = exception; }
            finally
            {
                Volatile.Write(ref accepts, 0);
                channel.Writer.TryComplete();
                lock (gate)
                {
                    running = false;
                }
            }
            lock (gate)
            {
                if (owned is not null)
                {
                    try { owned.Dispose(); owned = null; }
                    catch (Exception exception) when (exception is not OutOfMemoryException)
                    {
                        ReleaseFailed(logger);
                        activity.Complete(HostOperationOutcome.Failed);
                        throw new AggregateException("Communications subscription release failed and remains quarantined.",
                            failure is null ? [exception] : [failure, exception]);
                    }
                }
            }
            activity.Complete(failure is OperationCanceledException && token.IsCancellationRequested
                ? HostOperationOutcome.Cancelled : failure is null ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
            if (failure is not null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw(); }
        }

        public ValueTask DisposeAsync()
        {
            lock (gate)
            {
                disposed = true;
                if (running) { throw new InvalidOperationException("Join the communications observation worker before disposal."); }
                if (owned is not null)
                {
                    owned.Dispose();
                    owned = null;
                }
            }
            return ValueTask.CompletedTask;
        }
    }
}