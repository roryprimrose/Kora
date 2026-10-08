using AwesomeAssertions;
using System.Diagnostics;
using Kora.Core.Diagnostics;

using Kora.Core.Platform;
using Kora.Core.Voice;
using Kora.Windows.Session;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Session;

public sealed class WindowsPrivacyObservationServiceTests
{
    [Theory]
    [InlineData(WindowsSessionState.Unknown)]
    [InlineData(WindowsSessionState.Locked)]
    [InlineData(WindowsSessionState.Disconnected)]
    [InlineData(WindowsSessionState.SignedOut)]
    [InlineData(WindowsSessionState.Suspended)]
    public void Startup_uses_authoritative_state_and_never_defaults_to_unlocked(WindowsSessionState state)
    {
        using var source = new FakeSource { Snapshot = Ready with { SessionState = state } };
        using var observer = Create(source);

        observer.Current.SessionState.Should().Be(state);
        observer.Current.CanCapture.Should().BeFalse();
    }

    [Theory]
    [InlineData(WindowsSessionState.Locked)]
    [InlineData(WindowsSessionState.Disconnected)]
    [InlineData(WindowsSessionState.Suspended)]
    [InlineData(WindowsSessionState.SignedOut)]
    public void Negative_notification_is_synchronous_and_precedes_slow_state_query(WindowsSessionState state)
    {
        using var source = new FakeSource { Snapshot = Ready };
        using var observer = Create(source);
        var notified = false;
        observer.Changed += (_, args) => notified = args.Current.SessionState == state;
        source.BeforeRead = () => notified.Should().BeTrue();

        source.Notify(state);

        notified.Should().BeTrue();
        observer.Refresh().SessionState.Should().Be(state);
    }

    [Fact]
    public void Unlock_or_resume_requeries_OS_rather_than_trusting_notification_as_authority()
    {
        using var source = new FakeSource { Snapshot = Ready };
        using var observer = Create(source);
        source.Notify(WindowsSessionState.Locked);
        source.Snapshot = Ready with { SessionState = WindowsSessionState.Unknown };

        source.Notify(WindowsSessionState.Unlocked);

        observer.Current.SessionState.Should().Be(WindowsSessionState.Unknown);
    }

    [Fact]
    public void Permission_poll_and_endpoint_notifications_publish_fresh_revisions()
    {
        using var source = new FakeSource { Snapshot = Ready };
        using var observer = Create(source);
        var revision = observer.Current.TopologyRevision;
        source.Snapshot = Ready with { MicrophoneAccess = MicrophoneAccessState.Denied };

        observer.Refresh().CanCapture.Should().BeFalse();
        observer.Current.TopologyRevision.Should().Be(revision);
        source.Snapshot = Ready with { DefaultMicrophoneId = "other", ActiveMicrophoneIds = ["other"] };
        source.Notify(topology: true);

        observer.Current.TopologyRevision.Should().BeGreaterThan(revision);
        observer.Current.DefaultMicrophoneId.Should().Be("other");
    }

    [Fact]
    public void Query_failure_closes_all_privacy_prerequisites()
    {
        using var source = new FakeSource { Snapshot = Ready };
        using var observer = Create(source);
        source.BeforeRead = () => throw new InvalidOperationException("Synthetic OS failure");

        var result = observer.Refresh();

        result.SessionState.Should().Be(WindowsSessionState.Unknown);
        result.MicrophoneAccess.Should().Be(MicrophoneAccessState.Unknown);
        result.ActiveMicrophoneIds.Should().BeEmpty();
    }

    [Fact]
    public void One_faulting_consumer_cannot_skip_another_synchronous_privacy_gate()
    {
        using var source = new FakeSource { Snapshot = Ready };
        using var observer = Create(source);
        var gateClosed = false;
        observer.Changed += (_, _) => throw new InvalidOperationException("Synthetic consumer failure");
        observer.Changed += (_, args) => gateClosed = !args.Current.CanCapture;

        source.Notify(WindowsSessionState.Locked);

        gateClosed.Should().BeTrue();
    }

    [Fact]
    public async Task A_slow_refresh_cannot_delay_a_negative_session_gate()
    {
        using var source = new FakeSource { Snapshot = Ready };
        using var observer = Create(source);
        using var releaseRead = new ManualResetEventSlim();
        var readEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var negativeObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.BeforeRead = () =>
        {
            readEntered.TrySetResult();
            releaseRead.Wait(TestContext.Current.CancellationToken);
        };
        observer.Changed += (_, args) =>
        {
            if (args.Current.SessionState == WindowsSessionState.Locked)
            {
                negativeObserved.TrySetResult();
            }
        };
        var refresh = Task.Run(observer.Refresh, TestContext.Current.CancellationToken);
        await readEntered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        var notification = Task.Run(() => source.Notify(WindowsSessionState.Locked), TestContext.Current.CancellationToken);
        try
        {
            await negativeObserved.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            observer.Current.SessionState.Should().Be(WindowsSessionState.Locked);
        }
        finally
        {
            releaseRead.Set();
        }

        await Task.WhenAll(refresh, notification);
        observer.Current.SessionState.Should().Be(WindowsSessionState.Locked);
    }

    [Fact]
    public void Session_and_power_reasons_arrive_before_the_following_probe()
    {
        using var source = new FakeSource { Snapshot = Ready };
        using var observer = Create(source);
        WindowsPrivacyChangedEventArgs? observed = null;
        var beforeReadReason = WindowsPrivacyChangeReason.Unknown;
        observer.Changed += (_, args) => observed = args;
        source.BeforeRead = () => beforeReadReason = observed?.Reason ?? WindowsPrivacyChangeReason.Unknown;

        source.Notify(WindowsSessionState.Suspended, reason: WindowsPrivacyChangeReason.Power);

        (beforeReadReason & WindowsPrivacyChangeReason.Power).Should().Be(WindowsPrivacyChangeReason.Power);
        (beforeReadReason & WindowsPrivacyChangeReason.Session).Should().Be(WindowsPrivacyChangeReason.Session);
    }

    [Theory]
    [InlineData(WindowsPrivacyChangeReason.InputTopology)]
    [InlineData(WindowsPrivacyChangeReason.OutputTopology)]
    public void Endpoint_reasons_preserve_input_output_classification(WindowsPrivacyChangeReason reason)
    {
        using var source = new FakeSource { Snapshot = Ready };
        using var observer = Create(source);
        WindowsPrivacyChangedEventArgs? observed = null;
        observer.Changed += (_, args) => observed = args;

        source.Notify(topology: true, reason: reason);

        observed.Should().NotBeNull();
        (observed!.Reason & reason).Should().Be(reason);
        (observed.Reason & WindowsPrivacyChangeReason.DeviceTopology).Should().Be(WindowsPrivacyChangeReason.DeviceTopology);
    }

    [Fact]
    public void Default_speaker_change_is_distinguishable_from_microphone_default_change()
    {
        using var source = new FakeSource { Snapshot = Ready };
        using var observer = Create(source);
        WindowsPrivacyChangedEventArgs? observed = null;
        observer.Changed += (_, args) => observed = args;
        source.Snapshot = Ready with { DefaultSpeakerId = "new speaker" };

        source.Notify(topology: true, reason: WindowsPrivacyChangeReason.OutputTopology);

        observed.Should().NotBeNull();
        (observed!.Reason & WindowsPrivacyChangeReason.DefaultSpeaker).Should().Be(WindowsPrivacyChangeReason.DefaultSpeaker);
        (observed.Reason & WindowsPrivacyChangeReason.DefaultMicrophone).Should().Be(WindowsPrivacyChangeReason.Unknown);
    }

    [Fact]
    public void Permission_poll_reports_a_permission_reason_without_claiming_session_change()
    {
        using var source = new FakeSource { Snapshot = Ready };
        var time = new PrivacyPollingTimeProvider();
        using var observer = new WindowsPrivacyObservationService(
            source, NullLogger.Instance, TimeSpan.FromSeconds(1), time);
        WindowsPrivacyChangedEventArgs? notification = null;
        observer.Changed += (_, args) => notification = args;
        source.Snapshot = Ready with { MicrophoneAccess = MicrophoneAccessState.Denied };

        time.Timer.DueTime.Should().Be(TimeSpan.FromSeconds(1));
        time.Timer.Period.Should().Be(TimeSpan.FromSeconds(1));
        time.Timer.Tick();
        notification.Should().NotBeNull();
        var observed = notification!;

        (observed.Reason & WindowsPrivacyChangeReason.MicrophonePermission).Should().Be(WindowsPrivacyChangeReason.MicrophonePermission);
        (observed.Reason & WindowsPrivacyChangeReason.Polling).Should().Be(WindowsPrivacyChangeReason.Polling);
        (observed.Reason & WindowsPrivacyChangeReason.Session).Should().Be(WindowsPrivacyChangeReason.Unknown);
    }

    [Fact]
    public void Disposal_cancels_polling_and_queued_ticks_and_native_callbacks_cannot_read_or_publish()
    {
        using var source = new FakeSource { Snapshot = Ready };
        var time = new PrivacyPollingTimeProvider();
        using var observer = new WindowsPrivacyObservationService(
            source, NullLogger.Instance, TimeSpan.FromSeconds(1), time);
        var queuedNotification = source.QueueNotification(WindowsSessionState.Locked);
        var changes = new List<WindowsPrivacyChangedEventArgs>();
        observer.Changed += (_, args) => changes.Add(args);
        observer.Dispose();
        observer.Dispose();
        source.BeforeRead = () => throw new InvalidOperationException("Read after disposal");

        time.Timer.Tick();
        queuedNotification();
        observer.Refresh().Should().Be(observer.Current);

        time.Timer.IsDisposed.Should().BeTrue();
        source.ReadCalls.Should().Be(1);
        source.DisposeCalls.Should().Be(1);
        changes.Should().BeEmpty();
    }

    [Fact]
    public async Task Disposal_does_not_destroy_the_source_while_a_query_is_using_it()
    {
        using var source = new FakeSource { Snapshot = Ready };
        var time = new PrivacyPollingTimeProvider();
        using var observer = new WindowsPrivacyObservationService(
            source, NullLogger.Instance, TimeSpan.FromSeconds(1), time);
        using var releaseRead = new ManualResetEventSlim();
        var readEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposalEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        time.Timer.BeforeDispose = () => disposalEntered.TrySetResult();
        var disposedDuringRead = false;
        source.BeforeRead = () =>
        {
            readEntered.TrySetResult();
            releaseRead.Wait(TestContext.Current.CancellationToken);
            disposedDuringRead = source.DisposeCalls != 0;
        };
        var changes = new List<WindowsPrivacyChangedEventArgs>();
        observer.Changed += (_, args) => changes.Add(args);
        source.Snapshot = Ready with { MicrophoneAccess = MicrophoneAccessState.Denied };
        var refresh = Task.Run(observer.Refresh, TestContext.Current.CancellationToken);
        await readEntered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        var disposal = Task.Run(observer.Dispose, TestContext.Current.CancellationToken);
        try
        {
            await disposalEntered.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            // Current remains readable while disposal waits for the native query.
            observer.Current.MicrophoneAccess.Should().Be(MicrophoneAccessState.Allowed);
        }
        finally
        {
            releaseRead.Set();
        }
        await Task.WhenAll(refresh, disposal);

        changes.Should().BeEmpty();
        disposedDuringRead.Should().BeFalse();
        source.DisposeCalls.Should().Be(1);
    }

    [Fact]
    public void An_unknown_notification_closes_first_and_then_requires_a_fresh_authoritative_observation()
    {
        using var source = new FakeSource { Snapshot = Ready };
        using var observer = Create(source);
        var observedStates = new List<WindowsSessionState>();
        observer.Changed += (_, args) => observedStates.Add(args.Current.SessionState);

        source.Notify(WindowsSessionState.Unknown);

        observedStates.Should().Equal(WindowsSessionState.Unknown, WindowsSessionState.Unlocked);
        observer.Current.SessionState.Should().Be(WindowsSessionState.Unlocked);
    }

    private static WindowsPrivacySnapshot Ready => new(
        WindowsSessionState.Unlocked, MicrophoneAccessState.Allowed, 0, ["mic"], "mic", "speaker");

    [Fact]
    public void Native_observation_identity_and_timestamp_reach_consumers_before_requery()
    {
        using var source = new FakeSource { Snapshot = Ready };
        using var observer = Create(source);
        var observation = new PrivacyObservation(Guid.NewGuid(), 123, 1000);
        WindowsPrivacyChangedEventArgs? received = null;
        observer.Changed += (_, args) => received = args;
        source.BeforeRead = () =>
        {
            received.Should().NotBeNull();
            received!.Observation.Should().BeSameAs(observation);
        };

        source.Notify(WindowsSessionState.Locked, observation: observation);

        received!.Observation.ObservedTimestamp.Should().Be(123);
        received.Observation.OsEventToNotificationDelayMilliseconds.Should().BeNull();
    }

    [Fact]
    public void Failed_query_is_fail_closed_and_its_activity_does_not_report_success()
    {
        using var source = new FakeSource { Snapshot = Ready };
        using var observer = Create(source);
        Activity? owned = null;
        ActivityStatusCode? status = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = activitySource => string.Equals(activitySource.Name, "Kora.Windows", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (ReferenceEquals(activity, owned))
                {
                    status = activity.Status;
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        source.BeforeRead = () =>
        {
            owned = HostActivity.RequireCurrent().Activity;
            throw new IOException("Synthetic source failure");
        };

        observer.Refresh().CanCapture.Should().BeFalse();

        status.Should().Be(ActivityStatusCode.Error);
        owned!.GetTagItem("kora.outcome").Should().Be(nameof(HostOperationOutcome.Failed));
    }

    private static WindowsPrivacyObservationService Create(FakeSource source) => new(source, NullLogger.Instance);

    private sealed class FakeSource : IWindowsPrivacySource
    {
        public event EventHandler<WindowsPrivacySignalEventArgs>? Changed;
        public WindowsPrivacySnapshot Snapshot { get; set; } = Ready;
        public Action? BeforeRead { get; set; }
        public int ReadCalls { get; private set; }
        public int DisposeCalls { get; private set; }

        public WindowsPrivacySnapshot Read()
        {
            ReadCalls++;
            BeforeRead?.Invoke();
            return Snapshot;
        }

        public void Notify(
            WindowsSessionState? state = null,
            bool topology = false,
            WindowsPrivacyChangeReason reason = WindowsPrivacyChangeReason.Unknown,
            PrivacyObservation? observation = null) =>
            Changed?.Invoke(this, new WindowsPrivacySignalEventArgs(state, topology, reason, observation));

        public void Dispose()
        {
            DisposeCalls++;
        }

        public Action QueueNotification(WindowsSessionState state)
        {
            var callback = Changed;
            return () => callback?.Invoke(this, new WindowsPrivacySignalEventArgs(state));
        }
    }
}
