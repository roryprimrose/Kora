using System.Diagnostics;
using Kora.Core.Diagnostics;
using Kora.Core.Platform;
using Kora.Core.Voice;
using Kora.Windows.Diagnostics;

using Microsoft.Extensions.Logging;

namespace Kora.Windows.Session;

/// <summary>
/// Observes WTS session notifications, power and endpoint changes without recording.
/// Microphone permission uses a one-second polling fallback; observations never enable capture.
/// </summary>
public sealed class WindowsPrivacyObservationService : IWindowsPrivacyObservationService
{
    private readonly Lock sync = new();
    private readonly Lock querySync = new();
    private readonly IWindowsPrivacySource source;
    private readonly ILogger logger;
    private readonly ITimer? poll;
    private WindowsPrivacySnapshot current = new(
        WindowsSessionState.Unknown, MicrophoneAccessState.Unknown, 0, [], null, null);
    private WindowsSessionState? sessionOverride;
    private long observationVersion;
    private bool disposed;

    public WindowsPrivacyObservationService(
        IMicrophoneAccessService microphoneAccess,
        ILogger<WindowsPrivacyObservationService> logger)
        : this(new WindowsPrivacySource(microphoneAccess, logger), logger, TimeSpan.FromSeconds(1))
    {
    }

    internal WindowsPrivacyObservationService(
        IWindowsPrivacySource source,
        ILogger logger,
        TimeSpan? pollingInterval = null,
        TimeProvider? timeProvider = null)
    {
        this.source = source;
        this.logger = logger;
        source.Changed += OnSourceChanged;
        Refresh();
        if (pollingInterval is { } interval)
        {
            poll = (timeProvider ?? TimeProvider.System).CreateTimer(
                _ => Refresh(topologyChanged: false, WindowsPrivacyChangeReason.Polling), null, interval, interval);
        }
    }

    public event EventHandler<WindowsPrivacyChangedEventArgs>? Changed;

    public WindowsPrivacySnapshot Current
    {
        get
        {
            lock (sync)
            {
                return current;
            }
        }
    }

    public WindowsPrivacySnapshot Refresh() => Refresh(topologyChanged: false);

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }

        poll?.Dispose();
        source.Changed -= OnSourceChanged;
        // Native observation resources must outlive any query already using them.
        lock (querySync)
        {
            source.Dispose();
        }
    }

    private WindowsPrivacySnapshot Refresh(
        bool topologyChanged,
        WindowsPrivacyChangeReason reason = WindowsPrivacyChangeReason.Unknown,
        PrivacyObservation? observation = null)
    {
        using var activity = HostActivity.BeginOperation(HostActivityLayer.Windows, HostOperation.Runtime);
        lock (querySync)
        {
            var result = ReadAndPublish(topologyChanged, reason, observation, out var outcome);
            activity.Complete(outcome);
            return result;
        }
    }

    private WindowsPrivacySnapshot ReadAndPublish(bool topologyChanged, WindowsPrivacyChangeReason reason,
        PrivacyObservation? observation, out HostOperationOutcome outcome)
    {
        outcome = HostOperationOutcome.Cancelled;
        long requestedVersion;
        WindowsPrivacyChangedEventArgs? change;
        WindowsPrivacySnapshot result;
        lock (sync)
        {
            if (disposed)
            {
                return current;
            }

            requestedVersion = observationVersion;
        }

        WindowsPrivacySnapshot observed;
        var queryStarted = Stopwatch.GetTimestamp();
        try
        {
            observed = source.Read();
            outcome = HostOperationOutcome.Completed;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            outcome = HostOperationOutcome.Failed;
            WindowsLog.Error(logger, exception, "Observing Windows privacy prerequisites");
            observed = new WindowsPrivacySnapshot(
                WindowsSessionState.Unknown, MicrophoneAccessState.Unknown,
                0, [], null, null);
        }
        var queryCompleted = Stopwatch.GetTimestamp();

        lock (sync)
        {
            if (disposed || requestedVersion != observationVersion)
            {
                outcome = HostOperationOutcome.Cancelled;
                return current;
            }

            var changedEndpoints = topologyChanged ||
                !current.ActiveMicrophoneIds.SequenceEqual(observed.ActiveMicrophoneIds, StringComparer.Ordinal) ||
                !string.Equals(current.DefaultMicrophoneId, observed.DefaultMicrophoneId, StringComparison.Ordinal) ||
                !string.Equals(current.DefaultSpeakerId, observed.DefaultSpeakerId, StringComparison.Ordinal);
            if (sessionOverride == WindowsSessionState.Unknown && observed.SessionState != WindowsSessionState.Unknown)
            {
                sessionOverride = null;
            }

            observed = observed with
            {
                SessionState = sessionOverride ?? observed.SessionState,
                TopologyRevision = current.TopologyRevision + (changedEndpoints ? 1 : 0),
                ActiveMicrophoneIds = Array.AsReadOnly(observed.ActiveMicrophoneIds.ToArray()),
            };
            change = Update(observed, reason, observation ?? PrivacyObservation.Create(queryCompleted));
            result = current;
        }

        Notify(change);
        if (change is not null)
        {
            WindowsLog.PrivacyQueryCompleted(logger, change.Observation.Id, reason,
                queryStarted, queryCompleted, Stopwatch.Frequency);
        }
        return result;
    }

    private void OnSourceChanged(object? sender, WindowsPrivacySignalEventArgs eventArgs)
    {
        using var activity = HostActivity.BeginOperation(HostActivityLayer.Windows, HostOperation.Runtime);
        WindowsPrivacyChangedEventArgs? change = null;
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            observationVersion++;
            if (eventArgs.SessionState is { } state)
            {
                // A negative OS event closes consumers before querying slower device/registry state.
                sessionOverride = state == WindowsSessionState.Unlocked ? null : state;
                if (sessionOverride is not null)
                {
                    change = Update(current with { SessionState = state },
                        eventArgs.Reason == WindowsPrivacyChangeReason.Unknown
                            ? WindowsPrivacyChangeReason.Session
                            : eventArgs.Reason, eventArgs.Observation);
                }
            }
        }

        Notify(change);
        Refresh(eventArgs.TopologyChanged, eventArgs.Reason |
            (eventArgs.TopologyChanged ? WindowsPrivacyChangeReason.DeviceTopology : WindowsPrivacyChangeReason.Unknown),
            eventArgs.Observation);
        activity.Complete(HostOperationOutcome.Completed);
    }

    private WindowsPrivacyChangedEventArgs? Update(
        WindowsPrivacySnapshot observed,
        WindowsPrivacyChangeReason reason,
        PrivacyObservation observation)
    {
        if (current.SessionState == observed.SessionState &&
            current.MicrophoneAccess == observed.MicrophoneAccess &&
            current.TopologyRevision == observed.TopologyRevision)
        {
            return null;
        }

        var previous = current;
        current = observed;
        if (previous.SessionState != current.SessionState)
        {
            reason |= WindowsPrivacyChangeReason.Session;
        }

        if (previous.MicrophoneAccess != current.MicrophoneAccess)
        {
            reason |= WindowsPrivacyChangeReason.MicrophonePermission;
        }

        if (previous.TopologyRevision != current.TopologyRevision)
        {
            reason |= WindowsPrivacyChangeReason.DeviceTopology;
        }

        if (!string.Equals(previous.DefaultMicrophoneId, current.DefaultMicrophoneId, StringComparison.Ordinal))
        {
            reason |= WindowsPrivacyChangeReason.DefaultMicrophone;
        }

        if (!string.Equals(previous.DefaultSpeakerId, current.DefaultSpeakerId, StringComparison.Ordinal))
        {
            reason |= WindowsPrivacyChangeReason.DefaultSpeaker;
        }

        return new WindowsPrivacyChangedEventArgs(previous, current, reason, observation);
    }

    private void Notify(WindowsPrivacyChangedEventArgs? args)
    {
        if (args is null)
        {
            return;
        }

        using var scope = logger.BeginScope(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["PrivacyObservationId"] = args.Observation.Id,
        });
        foreach (var callback in Changed?.GetInvocationList() ?? [])
        {
            try
            {
                ((EventHandler<WindowsPrivacyChangedEventArgs>)callback)(this, args);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                WindowsLog.Error(logger, exception, "Notifying a Windows privacy observer");
            }
        }
        // Receipt emission follows synchronous negative closure, never delaying its admission gate.
        WindowsLog.PrivacyObserved(logger, args.Observation.Id, args.Reason,
            args.Current.SessionState, args.Current.MicrophoneAccess, args.Current.TopologyRevision,
            args.Observation.ObservedTimestamp, args.Observation.TimestampFrequency,
            args.Observation.OsEventToNotificationDelayMilliseconds);
    }
}
