using Avalonia;
using Avalonia.Controls;

using AwesomeAssertions;
using System.Diagnostics.Metrics;
using Kora.Core.Diagnostics;
using Kora.Windows.IntegrationTests.Storage;

namespace Kora.Windows.IntegrationTests.Presentation;

[Collection(nameof(ProcessGlobalEvidenceTestGroup))]
public sealed class PresenceWindowControllerTests
{
    [Fact]
    public void Phase_timing_distinguishes_owner_wait_fade_wait_and_ordered_replacement_without_poll_spam()
    {
        var phases = new List<PresenceSwitchPhase>();
        using var timing = new MeterListener();
        var instrument = PresenceSwitchTiming.Instrument;
        timing.InstrumentPublished = (published, observer) =>
        {
            if (ReferenceEquals(published, instrument)) { observer.EnableMeasurementEvents(published); }
        };
        timing.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            if (tags[0].Value is PresenceSwitchPhase phase) { phases.Add(phase); }
        });
        timing.Start();
        var fixture = new Fixture();
        fixture.Controller.Start(false);
        fixture.OwnerAvailable = false;
        fixture.Controller.Request(true);
        for (var index = 0; index < 10; index++) { fixture.Controller.IsReadyToApply.Should().BeFalse(); }
        fixture.OwnerAvailable = true;
        fixture.Windows[0].FadeReady = false;
        fixture.Controller.TryApply().Should().BeFalse();
        for (var index = 0; index < 10; index++) { fixture.Controller.IsReadyToApply.Should().BeFalse(); }
        fixture.Windows[0].FadeReady = true;
        fixture.Controller.TryApply().Should().BeTrue();
        phases.Should().Equal(PresenceSwitchPhase.WaitingForOwnerOrWindow,
            PresenceSwitchPhase.WaitingForFade, PresenceSwitchPhase.RetirementStarted,
            PresenceSwitchPhase.RetirementReturned, PresenceSwitchPhase.CreationStarted,
            PresenceSwitchPhase.CreationReturned, PresenceSwitchPhase.RestoreStarted, PresenceSwitchPhase.RestoreReturned);
    }

    [Fact]
    public void Repeated_switches_retire_before_creation_and_never_repeat_host_startup()
    {
        var fixture = new Fixture();
        fixture.Controller.Start(false);
        for (var index = 0; index < 20; index++)
        {
            var previous = fixture.Windows[^1];
            var native = index % 2 == 0;
            fixture.Controller.Request(native);
            fixture.Controller.IsReadyToApply.Should().BeTrue();
            fixture.Controller.TryApply().Should().BeTrue();
            previous.Closed.Should().BeTrue();
            fixture.Windows[^1].Restored.Should().Be(previous.State);
            fixture.Windows[^1].Native.Should().Be(native);
            fixture.Controller.IsAvailable.Should().BeTrue();
            fixture.Controller.HasPendingChange.Should().BeFalse();
        }
        fixture.Creations.Count(item => item.Startup).Should().Be(1);
        fixture.Creations.Should().HaveCount(21);
        fixture.Events.Take(4).Should().Equal("create:False", "suspend", "close", "create:True");
    }

    [Fact]
    public void Initializing_gesturing_or_owned_dialogs_defer_without_suspending_and_latest_choice_wins()
    {
        var fixture = new Fixture();
        fixture.Controller.Start(false);
        fixture.Windows[0].CanReplace = false;
        fixture.Controller.Request(true);
        fixture.Controller.IsReadyToApply.Should().BeFalse();
        fixture.Controller.TryApply().Should().BeFalse();
        fixture.Controller.Request(false);
        fixture.Controller.HasPendingChange.Should().BeFalse();
        fixture.Controller.Request(true);
        fixture.Windows[0].CanReplace = true;
        fixture.OwnerAvailable = false;
        fixture.Controller.IsReadyToApply.Should().BeFalse();
        fixture.Controller.TryApply().Should().BeFalse();
        fixture.Events.Should().Equal("create:False");
        fixture.OwnerAvailable = true;
        fixture.Controller.TryApply().Should().BeTrue();
        fixture.Creations.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(false, WindowState.Normal)]
    [InlineData(true, WindowState.Minimized)]
    [InlineData(true, WindowState.Normal)]
    public void Replacement_receives_exact_visibility_position_and_deadline_state(bool visible, WindowState windowState)
    {
        var fixture = new Fixture();
        fixture.Controller.Start(false);
        var original = fixture.Windows[0];
        original.State = new(new PixelPoint(420, -150), windowState, visible, visible,
            TimeSpan.FromMilliseconds(8123), 1234);
        fixture.Controller.Request(true);
        fixture.Controller.TryApply().Should().BeTrue();
        fixture.Windows[^1].Restored.Should().BeSameAs(original.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_retirement_never_creates_a_second_renderer_or_retries_uncertain_teardown(bool failSuspend)
    {
        var fixture = new Fixture();
        fixture.Controller.Start(false);
        fixture.Windows[0].FailSuspend = failSuspend;
        fixture.Windows[0].FailClose = !failSuspend;
        fixture.Controller.Request(true);
        var apply = fixture.Controller.TryApply;
        apply.Should().Throw<InvalidOperationException>();
        fixture.Controller.IsAvailable.Should().BeFalse();
        fixture.Controller.Request(true);
        apply.Should().Throw<InvalidOperationException>().WithMessage("*could not be retired*");
        fixture.Creations.Should().ContainSingle();
    }

    [Fact]
    public void Creation_failure_does_not_fall_back_and_a_fresh_choice_can_recover_without_reinitialization()
    {
        var fixture = new Fixture();
        fixture.Controller.Start(false);
        fixture.FailCreation = true;
        fixture.Controller.Request(true);
        var apply = fixture.Controller.TryApply;
        apply.Should().Throw<InvalidOperationException>().WithMessage("create failure");
        fixture.Controller.IsAvailable.Should().BeFalse();
        fixture.Controller.HasPendingChange.Should().BeFalse();
        fixture.Windows.Should().ContainSingle().Which.Closed.Should().BeTrue();
        fixture.FailCreation = false;
        fixture.Controller.Request(false);
        fixture.Controller.TryApply().Should().BeTrue();
        fixture.Windows[^1].Restored.Should().NotBeNull();
        fixture.Creations.Count(item => item.Startup).Should().Be(1);
    }

    [Fact]
    public void Failed_restore_is_closed_and_uncertain_cleanup_blocks_new_windows()
    {
        var fixture = new Fixture();
        fixture.Controller.Start(false);
        fixture.OnCreated = window => { window.FailRestore = true; window.FailClose = true; };
        fixture.Controller.Request(true);
        var apply = fixture.Controller.TryApply;
        apply.Should().Throw<AggregateException>().Which.InnerExceptions.Should().HaveCount(2);
        fixture.Controller.Request(false);
        apply.Should().Throw<InvalidOperationException>().WithMessage("*could not be retired*");
        fixture.Creations.Should().HaveCount(2);
        fixture.Controller.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void Failed_restore_with_successful_cleanup_keeps_snapshot_for_explicit_recovery()
    {
        var fixture = new Fixture();
        fixture.Controller.Start(false);
        fixture.OnCreated = window => window.FailRestore = true;
        fixture.Controller.Request(true);
        var apply = fixture.Controller.TryApply;
        apply.Should().Throw<InvalidOperationException>().WithMessage("restore failure");
        fixture.Windows[^1].Closed.Should().BeTrue();
        fixture.OnCreated = null;
        fixture.Controller.Request(false);
        fixture.Controller.TryApply().Should().BeTrue();
        fixture.Windows[^1].Restored.Should().Be(fixture.Windows[0].State);
    }

    [Fact]
    public void Reentrant_latest_request_is_applied_after_current_retirement_completes()
    {
        var fixture = new Fixture();
        fixture.Controller.Start(false);
        fixture.OnCreated = _ => fixture.Controller.Request(false);
        fixture.Controller.Request(true);
        fixture.Controller.TryApply().Should().BeTrue();
        fixture.Controller.HasPendingChange.Should().BeTrue();
        fixture.OnCreated = null;
        fixture.Controller.TryApply().Should().BeTrue();
        fixture.Windows[^1].Native.Should().BeFalse();
    }

    [Fact]
    public void Shutdown_during_retirement_does_not_create_a_replacement()
    {
        var fixture = new Fixture();
        fixture.Controller.Start(false);
        fixture.Windows[0].OnClosed = fixture.Controller.Stop;
        fixture.Controller.Request(true);
        fixture.Controller.TryApply().Should().BeTrue();
        fixture.Windows[0].Closed.Should().BeTrue();
        fixture.Creations.Should().ContainSingle();
        fixture.Controller.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void Shutdown_during_creation_closes_candidate_without_revealing_it()
    {
        var fixture = new Fixture();
        fixture.Controller.Start(false);
        fixture.OnCreated = _ => fixture.Controller.Stop();
        fixture.Controller.Request(true);
        fixture.Controller.TryApply().Should().BeTrue();
        fixture.Windows[^1].Restored.Should().BeNull();
        fixture.Windows[^1].Closed.Should().BeTrue();
        fixture.Controller.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void Stopping_cancels_pending_changes_and_same_renderer_selection_does_no_work()
    {
        var fixture = new Fixture();
        fixture.Controller.Start(false);
        fixture.Controller.Request(false);
        fixture.Controller.TryApply().Should().BeTrue();
        fixture.Controller.Request(true);
        fixture.Controller.Stop();
        fixture.Controller.Request(true);
        fixture.Controller.IsReadyToApply.Should().BeFalse();
        fixture.Controller.TryApply().Should().BeTrue();
        fixture.Creations.Should().ContainSingle();
        var startAgain = () => fixture.Controller.Start(true);
        startAgain.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Outgoing_fade_keeps_one_live_owner_and_does_not_retire_until_ready()
    {
        var fixture = new Fixture();
        fixture.Controller.Start(false);
        var original = fixture.Windows[0];
        original.FadeReady = false;
        fixture.Controller.Request(true);
        for (var index = 0; index < 10; index++)
        {
            fixture.Controller.TryApply().Should().BeFalse();
            fixture.Controller.IsAvailable.Should().BeTrue();
            fixture.Controller.HasPendingChange.Should().BeTrue();
            original.Closed.Should().BeFalse();
            fixture.Creations.Should().ContainSingle();
        }
        original.FadeStarted.Should().BeTrue();
        original.Preparations.Should().Be(1, "waiting polls must remain read-only");
        original.FadeReady = true;
        fixture.Controller.TryApply().Should().BeTrue();
        original.Closed.Should().BeTrue();
        fixture.Windows[^1].Restored.Should().BeSameAs(original.State);
        fixture.Creations.Should().HaveCount(2);
    }

    [Fact]
    public void Selecting_current_renderer_during_fade_resumes_it_without_replacement()
    {
        var fixture = new Fixture();
        fixture.Controller.Start(false);
        var original = fixture.Windows[0];
        original.FadeReady = false;
        fixture.Controller.Request(true);
        fixture.Controller.TryApply().Should().BeFalse();
        fixture.Controller.Request(false);
        fixture.Controller.TryApply().Should().BeTrue();
        original.FadeCancelled.Should().BeTrue();
        original.Closed.Should().BeFalse();
        fixture.Controller.HasPendingChange.Should().BeFalse();
        fixture.Creations.Should().ContainSingle();
    }

    [Fact]
    public void New_owned_dialog_defers_retirement_after_fade_and_stop_never_reveals_or_replaces()
    {
        var fixture = new Fixture();
        fixture.Controller.Start(false);
        var original = fixture.Windows[0];
        original.FadeReady = false;
        fixture.Controller.Request(true);
        fixture.Controller.TryApply().Should().BeFalse();
        original.FadeReady = true;
        original.CanReplace = false;
        fixture.Controller.TryApply().Should().BeFalse();
        original.CanReplace = true;
        fixture.OwnerAvailable = false;
        fixture.Controller.TryApply().Should().BeFalse();
        fixture.Controller.Stop();
        fixture.OwnerAvailable = true;
        fixture.Controller.TryApply().Should().BeTrue();
        original.FadeCancelled.Should().BeFalse();
        original.Closed.Should().BeFalse();
        fixture.Creations.Should().ContainSingle();
        fixture.Controller.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void Failed_fade_does_not_retire_and_an_explicit_current_choice_can_cancel_it()
    {
        var fixture = new Fixture();
        fixture.Controller.Start(false);
        var original = fixture.Windows[0];
        original.FailFade = true;
        fixture.Controller.Request(true);
        var apply = fixture.Controller.TryApply;
        apply.Should().Throw<TimeoutException>().WithMessage("fade failure");
        original.Closed.Should().BeFalse();
        fixture.Creations.Should().ContainSingle();
        fixture.Controller.Request(false);
        fixture.Controller.TryApply().Should().BeTrue();
        original.FadeCancelled.Should().BeTrue();
        fixture.Controller.HasPendingChange.Should().BeFalse();
    }

    private sealed class Fixture
    {
        public List<string> Events { get; } = [];
        public List<Surface> Windows { get; } = [];
        public List<(bool Native, bool Startup)> Creations { get; } = [];
        public PresenceWindowController Controller { get; }
        public bool OwnerAvailable { get; set; } = true;
        public bool FailCreation { get; set; }
        public Action<Surface>? OnCreated { get; set; }

        public Fixture() => Controller = new(Create, () => OwnerAvailable);

        private IPresenceWindow Create(bool native, bool startup)
        {
            if (Windows.Count > 0 && !Windows[^1].FailClose)
            {
                Windows[^1].Closed.Should().BeTrue("creation must follow successful old-worker/window teardown");
            }
            Creations.Add((native, startup));
            Events.Add($"create:{native}");
            if (FailCreation) { throw new InvalidOperationException("create failure"); }
            var surface = new Surface(Events, native);
            Windows.Add(surface);
            OnCreated?.Invoke(surface);
            return surface;
        }
    }

    private sealed class Surface(List<string> events, bool native) : IPresenceWindow
    {
        public bool Native { get; } = native;
        public bool CanReplace { get; set; } = true;
        public bool Closed { get; private set; }
        public bool FailSuspend { get; set; }
        public bool FailClose { get; set; }
        public bool FailRestore { get; set; }
        public bool FadeReady { get; set; } = true;
        public bool FadeStarted { get; private set; }
        public bool FadeCancelled { get; private set; }
        public bool FailFade { get; set; }
        public int Preparations { get; private set; }
        public bool IsReplacementReady => !FadeStarted || FadeReady || FailFade;
        public Action? OnClosed { get; set; }
        public PresenceWindowState State { get; set; } =
            new(new PixelPoint(100, 150), WindowState.Normal, true, true, TimeSpan.FromSeconds(5), 0);
        public PresenceWindowState? Restored { get; private set; }

        public bool PrepareForReplacement()
        {
            FadeStarted = true;
            Preparations++;
            if (FailFade) { throw new TimeoutException("fade failure"); }
            return FadeReady;
        }
        public void CancelReplacement() => FadeCancelled = true;

        public PresenceWindowState SuspendForReplacement()
        {
            events.Add("suspend");
            if (FailSuspend) { throw new InvalidOperationException("suspend failure"); }
            return State;
        }
        public void CloseForReplacement()
        {
            events.Add("close");
            if (FailClose) { throw new InvalidOperationException("close failure"); }
            Closed = true;
            OnClosed?.Invoke();
        }
        public void RestoreAfterReplacement(PresenceWindowState state)
        {
            if (FailRestore) { throw new InvalidOperationException("restore failure"); }
            Restored = State = state;
        }
    }
}
