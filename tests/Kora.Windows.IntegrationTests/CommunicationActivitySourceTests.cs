using System.Threading.Channels;
using System.Runtime.InteropServices;

using AwesomeAssertions;

using Kora.Core.Communication;
using Kora.Windows.Communication;
using Kora.Windows.IntegrationTests.Session;

using Microsoft.Extensions.Logging.Abstractions;

using static Kora.Windows.Communication.WindowsCommunicationsActivitySource;

namespace Kora.Windows.IntegrationTests;

public sealed class CommunicationActivitySourceTests
{
    [Fact]
    public async Task Production_profile_is_unavailable_and_never_opens_a_native_source()
    {
        await using var source = new WindowsCommunicationsActivitySource(TimeProvider.System,
            NullLogger<WindowsCommunicationsActivitySource>.Instance);
        source.IsQualified.Should().BeFalse();
        source.HasUnreleasedResources.Should().BeFalse();
        var operation = () => source.ObserveAsync(Guid.NewGuid(), _ => throw new InvalidOperationException("must not publish"),
            TestContext.Current.CancellationToken);
        await operation.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not qualified*");
    }

    [Fact]
    public void Native_callback_is_content_free_bounded_delivery_and_retirement_fences_late_notifications()
    {
        var events = new List<WindowsCommunicationSignal>();
        var callback = new WindowsCommunicationsNotificationPlatform.Callback(events.Add);
        callback.OnVolumeDuckNotification("opaque-session", 1).Should().Be(0);
        callback.OnVolumeUnduckNotification("opaque-session").Should().Be(0);
        events.Select(e => e.Kind).Should().Equal(WindowsCommunicationSignalKind.Started, WindowsCommunicationSignalKind.Ended);
        callback.Retire();
        callback.OnVolumeDuckNotification("late", 2).Should().Be(0);
        callback.OnVolumeUnduckNotification("late").Should().Be(0);
        events.Should().HaveCount(2);
        var pointer = Marshal.GetComInterfaceForObject(callback, typeof(IWindowsCommunicationsDuckNotification));
        try { pointer.Should().NotBe(0); }
        finally { Marshal.Release(pointer); }
    }

    [Fact]
    public async Task Initial_unknown_single_other_stream_suspected_witnessed_end_clear_and_healthy_heartbeat_have_one_device_epoch()
    {
        await using var f = new Fixture();
        (await f.Next()).Health.Should().Be(CommunicationActivityHealth.Starting);
        f.Platform.Emit(new(WindowsCommunicationSignalKind.Started, "one", 1));
        var active = await f.Next();
        active.State.Should().Be(CallState.Suspected);
        active.Generation.Should().Be(f.Generation);
        f.Platform.Emit(new(WindowsCommunicationSignalKind.Ended, "one", 0));
        var clear = await f.Next();
        clear.State.Should().Be(CallState.Clear);
        clear.DeviceEpoch.Should().Be(active.DeviceEpoch);
        clear.Sequence.Should().BeGreaterThan(active.Sequence);
        f.Time.Timer.Tick();
        (await f.Next()).State.Should().Be(CallState.Clear);
        await f.Stop();
        f.Platform.Lease.Disposals.Should().Be(1);
        f.Observer.HasUnreleasedResources.Should().BeFalse();
        f.Platform.Emit(new(WindowsCommunicationSignalKind.Started, "late", 1));
        f.Results.Reader.TryRead(out _).Should().BeFalse();
    }

    [Theory]
    [InlineData((int)WindowsCommunicationIdentity.Kora, 1, "self")]
    [InlineData((int)WindowsCommunicationIdentity.Unknown, 1, "unresolved")]
    [InlineData((int)WindowsCommunicationIdentity.Other, 0, "zero")]
    [InlineData((int)WindowsCommunicationIdentity.Other, 129, "bounded")]
    public async Task Own_unknown_and_invalid_activity_is_not_promoted_to_suspected_or_clear(
        int identity, uint count, string session)
    {
        await using var f = new Fixture();
        await f.Next();
        f.Platform.Lease.Identity = (WindowsCommunicationIdentity)identity;
        f.Platform.Emit(new(WindowsCommunicationSignalKind.Started, session, count));
        (await f.Next()).State.Should().Be(CallState.Unknown);
        f.Platform.Emit(new(WindowsCommunicationSignalKind.Ended, session, 0));
        (await f.Next()).State.Should().Be(CallState.Unknown);
    }

    [Theory]
    [InlineData("multiple")]
    [InlineData("foreign-end")]
    [InlineData("empty")]
    [InlineData("oversized")]
    [InlineData("invalid")]
    [InlineData("unknown-kind")]
    [InlineData("null")]
    public async Task Ambiguous_notifications_never_supply_clear(string kind)
    {
        await using var f = new Fixture();
        await f.Next();
        f.Platform.Emit(new(WindowsCommunicationSignalKind.Started, "one", kind is "multiple" ? 2u : 1u));
        await f.Next();
        var session = kind switch { "empty" => "", "null" => null, "oversized" => new string('x', 1025), "foreign-end" => "other", _ => "one" };
        f.Platform.Emit(new(kind switch
        {
            "invalid" => WindowsCommunicationSignalKind.Invalid,
            "unknown-kind" => (WindowsCommunicationSignalKind)99,
            _ => WindowsCommunicationSignalKind.Ended,
        }, session, 0));
        (await f.Next()).State.Should().Be(CallState.Unknown);
    }

    [Theory]
    [InlineData(CommunicationActivityHealth.DeviceLost)]
    [InlineData(CommunicationActivityHealth.SessionLost)]
    [InlineData(CommunicationActivityHealth.Failed)]
    public async Task Device_session_or_source_loss_publishes_unknown_and_joins_release(CommunicationActivityHealth health)
    {
        await using var f = new Fixture();
        await f.Next();
        f.Platform.Lease.Health = health;
        f.Time.Timer.Tick();
        var lost = await f.Next();
        lost.State.Should().Be(CallState.Unknown);
        lost.Health.Should().Be(health);
        await FluentActions.Awaiting(() => f.Work).Should().ThrowAsync<InvalidOperationException>();
        f.Platform.Lease.Disposals.Should().Be(1);
        f.Observer.HasUnreleasedResources.Should().BeFalse();
    }

    [Fact]
    public async Task Failed_release_is_quarantined_cannot_start_replacement_and_is_retried_by_disposal()
    {
        await using var f = new Fixture();
        await f.Next();
        f.Platform.Lease.ReleaseFailure = new IOException("deterministic unregister failure");
        await f.Cancel.CancelAsync();
        await FluentActions.Awaiting(() => f.Work).Should().ThrowAsync<AggregateException>();
        f.Observer.HasUnreleasedResources.Should().BeTrue();
        await FluentActions.Awaiting(() => f.Observer.RunAsync(Guid.NewGuid(), _ => { }, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(async () => await f.Observer.DisposeAsync()).Should().ThrowAsync<IOException>();
        f.Observer.HasUnreleasedResources.Should().BeTrue();
        f.Platform.Lease.ReleaseFailure = null;
        await f.Observer.DisposeAsync();
        f.Observer.HasUnreleasedResources.Should().BeFalse();
    }

    [Fact]
    public async Task Invalid_generation_disposal_during_work_and_open_failure_have_explicit_outcomes()
    {
        var platform = new Platform { OpenFailure = new IOException("open failed") };
        await using var observer = new WindowsCommunicationsObserver(platform, TimeProvider.System,
            NullLogger<WindowsCommunicationsActivitySource>.Instance);
        await FluentActions.Awaiting(() => observer.RunAsync(Guid.Empty, _ => { }, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => observer.RunAsync(Guid.NewGuid(), _ => { }, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<IOException>();
        observer.HasUnreleasedResources.Should().BeFalse();
        await using var f = new Fixture();
        await f.Next();
        await FluentActions.Awaiting(async () => await f.Observer.DisposeAsync()).Should().ThrowAsync<InvalidOperationException>();
        await f.Stop();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal Fixture()
        {
            Observer = new(Platform, Time, NullLogger<WindowsCommunicationsActivitySource>.Instance);
            Work = Observer.RunAsync(Generation, value => Results.Writer.TryWrite(value), Cancel.Token);
        }
        internal CancellationToken Token => TestContext.Current.CancellationToken;
        internal Guid Generation { get; } = Guid.NewGuid();
        internal PrivacyPollingTimeProvider Time { get; } = new();
        internal Platform Platform { get; } = new();
        internal CancellationTokenSource Cancel { get; } = new();
        internal Channel<CommunicationActivityObservation> Results { get; } = Channel.CreateUnbounded<CommunicationActivityObservation>();
        internal WindowsCommunicationsObserver Observer { get; }
        internal Task Work { get; }
        internal async Task<CommunicationActivityObservation> Next() =>
            await Results.Reader.ReadAsync(Token).AsTask().WaitAsync(TimeSpan.FromSeconds(10), Token);
        internal async Task Stop()
        {
            await Cancel.CancelAsync();
            try { await Work; }
            catch (OperationCanceledException) when (Cancel.IsCancellationRequested) { }
        }
        public async ValueTask DisposeAsync()
        {
            await Cancel.CancelAsync();
            try { await Work; }
            catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException or AggregateException) { }
            Platform.Lease.ReleaseFailure = null;
            await Observer.DisposeAsync();
            Cancel.Dispose();
        }
    }

    private sealed class Platform : IWindowsCommunicationsNotificationPlatform
    {
        private Action<WindowsCommunicationSignal>? publish;
        internal Lease Lease { get; } = new();
        internal Exception? OpenFailure { get; init; }
        public IWindowsCommunicationsNotificationLease Open(Action<WindowsCommunicationSignal> callback)
        {
            if (OpenFailure is { } failure) { throw failure; }
            publish = callback;
            return Lease;
        }
        internal void Emit(WindowsCommunicationSignal signal) => publish!(signal);
    }

    private sealed class Lease : IWindowsCommunicationsNotificationLease
    {
        internal WindowsCommunicationIdentity Identity { get; set; } = WindowsCommunicationIdentity.Other;
        internal CommunicationActivityHealth Health { get; set; } = CommunicationActivityHealth.Healthy;
        internal Exception? ReleaseFailure { get; set; }
        internal int Disposals { get; private set; }
        public Guid DeviceEpoch { get; } = Guid.NewGuid();
        public CommunicationActivityHealth ReadHealth() => Health;
        public WindowsCommunicationIdentity Resolve(string sessionId) => Identity;
        public void Dispose()
        {
            if (ReleaseFailure is { } failure) { throw failure; }
            Disposals++;
        }
    }
}