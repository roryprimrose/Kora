using AwesomeAssertions;

using Kora.Application.UnitTests.Configuration;
using Kora.Core.Communication;

namespace Kora.Application.UnitTests.Communication;

[Collection("Host tracing")]
public sealed class CommunicationActivityCallStateTests
{
    [Theory]
    [InlineData(CallState.Clear, false)]
    [InlineData(CallState.Suspected, true)]
    [InlineData(CallState.Unknown, true)]
    [InlineData(CallState.Active, true)]
    [InlineData(CallState.Unavailable, true)]
    [InlineData((CallState)99, true)]
    public async Task Only_declared_healthy_clear_and_suspected_states_are_consumed(CallState state, bool suppress)
    {
        await using var f = new CommunicationActivityFixture();
        await f.Initialize(CommunicationActivitySelection.WindowsCommunicationsAudio);
        f.Source.Signal(state, f.Time.GetUtcNow());
        f.Policy.Current.SuppressSpeech.Should().Be(suppress);
        var starts = 0;
        await f.Policy.StartSpeech(() => { starts++; return Task.CompletedTask; }, () => f.Eligible);
        starts.Should().Be(suppress ? 0 : 1);
        f.Observations.Refresh();
    }

    [Theory]
    [InlineData(CommunicationActivityHealth.Starting)]
    [InlineData(CommunicationActivityHealth.Unavailable)]
    [InlineData(CommunicationActivityHealth.Failed)]
    [InlineData(CommunicationActivityHealth.DeviceLost)]
    [InlineData(CommunicationActivityHealth.SessionLost)]
    [InlineData((CommunicationActivityHealth)99)]
    public async Task Health_uncertainty_never_normalizes_to_clear(CommunicationActivityHealth health)
    {
        await using var f = new CommunicationActivityFixture();
        await f.Initialize(CommunicationActivitySelection.WindowsCommunicationsAudio);
        f.Source.Signal(CallState.Clear, f.Time.GetUtcNow(), health);
        f.Policy.Current.SuppressSpeech.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4999)]
    [InlineData(5000)]
    [InlineData(6000)]
    public async Task Freshness_uses_exact_bound_and_future_observations_fail_closed(int milliseconds)
    {
        await using var f = new CommunicationActivityFixture();
        await f.Initialize(CommunicationActivitySelection.WindowsCommunicationsAudio);
        f.Source.Signal(CallState.Clear, f.Time.GetUtcNow());
        f.Time.Advance(TimeSpan.FromMilliseconds(milliseconds));
        f.Observations.CurrentState.Should().Be(milliseconds is >= 0 and < 5000 ? CallState.Clear : CallState.Unknown);
        f.Source.Signal(CallState.Clear, f.Time.GetUtcNow());
        f.Observations.CurrentState.Should().Be(CallState.Clear);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Consequential_admission_rechecks_freshness_and_host_epoch_even_before_timer_or_callback_dispatch(int change)
    {
        await using var f = new CommunicationActivityFixture();
        long revision = 1;
        long privacy = 1;
        f.Observations.BindHost(() => f.Eligible, () => (revision, privacy));
        f.Observations.ObservePreferences();
        await f.Observations.JoinAsync();
        await f.Select(CommunicationActivitySelection.WindowsCommunicationsAudio);
        f.Source.Signal(CallState.Clear, f.Time.GetUtcNow());
        if (change == 1) { revision++; }
        else if (change == 2) { privacy++; }
        else { f.Time.Advance(TimeSpan.FromSeconds(5), tick: false); }
        var starts = 0;
        await f.Policy.StartSpeech(() => { starts++; return Task.CompletedTask; }, () => f.Eligible);
        starts.Should().Be(0);
        f.Policy.Current.AutomaticState.Should().Be(CallState.Unknown);
        f.Observations.Refresh();
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("device")]
    [InlineData("sequence")]
    [InlineData("repeat")]
    public async Task Foreign_or_nonmonotonic_callbacks_cannot_clear_protection(string kind)
    {
        await using var f = new CommunicationActivityFixture();
        await f.Initialize(CommunicationActivitySelection.WindowsCommunicationsAudio);
        f.Source.Signal(CallState.Suspected, f.Time.GetUtcNow());
        f.Source.Publish!(new(kind is "generation" ? Guid.NewGuid() : f.Source.Generation,
            kind is "device" ? Guid.Empty : f.Source.DeviceEpoch, kind is "sequence" ? 0 : kind is "repeat" ? 2 : 3,
            CallState.Clear, f.Time.GetUtcNow(), CommunicationActivityHealth.Healthy));
        f.Policy.Current.SuppressSpeech.Should().BeTrue();
    }

    [Fact]
    public async Task Disable_waits_for_actual_source_release_and_rejects_late_old_generation_output()
    {
        await using var f = new CommunicationActivityFixture();
        f.Source.AutoRelease = false;
        await f.Initialize(CommunicationActivitySelection.WindowsCommunicationsAudio);
        f.Source.Signal(CallState.Suspected, f.Time.GetUtcNow());
        var late = f.Source.Publish!;
        var generation = f.Source.Generation;
        var selectedNone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Observations.Changed += (_, _) =>
        {
            if (f.Observations.Selection == CommunicationActivitySelection.None && f.Observations.PreferencesAvailable)
            { selectedNone.TrySetResult(); }
        };
        var disable = f.Select(CommunicationActivitySelection.None);
        await f.Source.Cancelled.Task.WaitAsync(f.Token);
        await selectedNone.Task.WaitAsync(f.Token);
        disable.IsCompleted.Should().BeFalse();
        f.Observations.Refresh();
        f.Observations.CurrentState.Should().Be(CallState.Unknown);
        late(new(generation, f.Source.DeviceEpoch, 999, CallState.Clear, f.Time.GetUtcNow(), CommunicationActivityHealth.Healthy));
        f.Policy.Current.SuppressSpeech.Should().BeTrue();
        f.Source.Release.TrySetResult();
        (await disable).Should().Be(CallMutationOutcome.Applied);
        f.Source.Releases.Should().Be(1);
        f.Observations.CurrentState.Should().Be(CallState.Unavailable);
        await f.Observations.DisposeAsync();
        f.Time.Advance(TimeSpan.FromSeconds(1));
        late(new(generation, f.Source.DeviceEpoch, 1000, CallState.Clear, f.Time.GetUtcNow(), CommunicationActivityHealth.Healthy));
        f.Observations.CurrentState.Should().Be(CallState.Unknown);
        FluentActions.Invoking(f.Observations.ObservePreferences).Should().Throw<ObjectDisposedException>();
        FluentActions.Invoking(() => f.Observations.BindHost(() => true)).Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task Lost_host_cancels_observation_and_does_not_reopen_capture_or_speech()
    {
        await using var f = new CommunicationActivityFixture();
        await f.Initialize(CommunicationActivitySelection.WindowsCommunicationsAudio);
        f.Source.Signal(CallState.Clear, f.Time.GetUtcNow());
        f.Eligible = false;
        f.Observations.Refresh();
        await f.Source.Cancelled.Task.WaitAsync(f.Token);
        await f.Observations.JoinAsync();
        f.Observations.CurrentState.Should().Be(CallState.Unknown);
        f.Source.Opens.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_source_or_release_is_unknown_and_unreleased_resources_remain_owned(bool resourcesHeld)
    {
        await using var f = new CommunicationActivityFixture();
        f.Source.OpenFailure = new IOException("platform failure");
        f.Source.HasUnreleasedResources = resourcesHeld;
        await f.Initialize(CommunicationActivitySelection.WindowsCommunicationsAudio);
        if (resourcesHeld)
        {
            await FluentActions.Awaiting(f.Observations.JoinAsync).Should().ThrowAsync<InvalidOperationException>();
            f.Observations.ResourceReleaseFailed.Should().BeTrue();
        }
        else { await f.Observations.JoinAsync(); }
        f.Observations.Observation!.Health.Should().Be(CommunicationActivityHealth.Failed);
        f.Observations.CurrentState.Should().Be(CallState.Unknown);
    }

    [Theory]
    [InlineData("unsupported")]
    [InlineData("owner")]
    [InlineData("missing")]
    [InlineData("zero-sequence")]
    public async Task Missing_support_scope_or_initial_sequence_stays_unknown(string stage)
    {
        await using var f = new CommunicationActivityFixture();
        if (stage is "unsupported") { f.Source.IsQualified = false; }
        if (stage is "owner") { f.Eligible = false; }
        if (stage is "zero-sequence") { f.Source.FirstSequence = 0; }
        if (stage is "missing")
        {
            f.Source.BeforeStart = () => f.Observations.CurrentState.Should().Be(CallState.Unknown);
        }
        await f.Initialize(CommunicationActivitySelection.WindowsCommunicationsAudio);
        f.Observations.CurrentState.Should().Be(CallState.Unknown);
    }

    [Fact]
    public async Task No_subscriber_and_disabled_source_refresh_remain_truthful_without_starting_native_work()
    {
        await using var f = new CommunicationActivityFixture();
        f.Policy.Dispose();
        await f.Initialize();
        f.Observations.Refresh();
        f.Observations.CurrentState.Should().Be(CallState.Unavailable);
        f.Observations.HoldUnavailable();
        f.Observations.Refresh();
        f.Observations.CurrentState.Should().Be(CallState.Unknown);
        f.Source.Opens.Should().Be(0);
    }

    [Fact]
    public async Task Epoch_replacement_racing_a_passive_snapshot_cannot_publish_the_previous_scope_as_clear()
    {
        await using var f = new CommunicationActivityFixture();
        var replace = false;
        using var published = new ManualResetEventSlim();
        (long Owner, long Privacy) Revision()
        {
            if (replace)
            {
                replace = false;
                f.Source.AfterStart = published.Set;
                f.Observations.BindHost(() => true, () => (2, 1));
                published.Wait(f.Token);
            }
            return (1, 1);
        }
        f.Observations.BindHost(() => true, Revision);
        f.Observations.ObservePreferences();
        await f.Observations.JoinAsync();
        await f.Select(CommunicationActivitySelection.WindowsCommunicationsAudio);
        f.Source.Signal(CallState.Clear, f.Time.GetUtcNow());
        replace = true;
        f.Observations.CurrentState.Should().Be(CallState.Unknown);
    }
}