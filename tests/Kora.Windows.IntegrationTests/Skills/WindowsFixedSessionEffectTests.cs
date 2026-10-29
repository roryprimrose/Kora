using AwesomeAssertions;

using Kora.Core.Skills;
using Kora.Windows.Skills;

namespace Kora.Windows.IntegrationTests.Skills;

public sealed class WindowsFixedSessionEffectTests
{
    [Theory]
    [InlineData(FixedSessionAction.Lock)]
    [InlineData(FixedSessionAction.Shutdown)]
    [InlineData(FixedSessionAction.Restart)]
    public void Unknown_interactive_session_never_acquires_authority_or_calls_Windows(FixedSessionAction action)
    {
        var platform = new Platform { Current = false };
        new WindowsFixedSessionEffect(platform).Request(action).Outcome.Should().Be(FixedSessionEffectOutcome.Denied);
        platform.Events.Should().Equal("current");
    }

    [Fact]
    public void Lock_uses_no_shutdown_privilege_and_reports_only_request_acceptance()
    {
        var platform = new Platform();
        new WindowsFixedSessionEffect(platform).Request(FixedSessionAction.Lock)
            .Should().Be(new FixedSessionEffectResult(FixedSessionEffectOutcome.RequestAccepted));
        platform.Events.Should().Equal("current", "lock");
    }

    [Fact]
    public void Unknown_action_is_rejected_without_any_platform_observation()
    {
        var platform = new Platform();
        ((Action)(() => new WindowsFixedSessionEffect(platform).Request((FixedSessionAction)99)))
            .Should().Throw<ArgumentOutOfRangeException>();
        platform.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData(FixedSessionAction.Shutdown)]
    [InlineData(FixedSessionAction.Restart)]
    public void Power_uses_one_private_assigned_authority_restores_then_disposes(FixedSessionAction action)
    {
        var platform = new Platform();
        new WindowsFixedSessionEffect(platform).Request(action).Outcome.Should().Be(FixedSessionEffectOutcome.RequestAccepted);
        platform.Events.Should().Equal("current", "duplicate", "enable", "current", action.ToString(), "restore", "dispose");
    }

    [Theory]
    [InlineData(1300)]
    [InlineData(5)]
    public void Missing_or_unavailable_assigned_privilege_never_requests_an_effect(int error)
    {
        var platform = new Platform { ActivationError = error };
        new WindowsFixedSessionEffect(platform).Request(FixedSessionAction.Shutdown)
            .Should().Be(new FixedSessionEffectResult(FixedSessionEffectOutcome.Denied, error));
        platform.Events.Should().Equal("current", "duplicate", "enable", "dispose");
    }

    [Fact]
    public void Authority_change_after_activation_is_denied_but_restores_private_state()
    {
        var platform = new Platform { RetireAfterActivation = true };
        new WindowsFixedSessionEffect(platform).Request(FixedSessionAction.Restart).Outcome
            .Should().Be(FixedSessionEffectOutcome.Denied);
        platform.Events.Should().Equal("current", "duplicate", "enable", "current", "restore", "dispose");
    }

    [Theory]
    [InlineData(FixedSessionEffectOutcome.RequestAccepted)]
    [InlineData(FixedSessionEffectOutcome.Failed)]
    public void Failed_restoration_never_reports_success_or_retries(FixedSessionEffectOutcome nativeOutcome)
    {
        var platform = new Platform { RestoreError = 5, NativeOutcome = nativeOutcome };
        new WindowsFixedSessionEffect(platform).Request(FixedSessionAction.Restart)
            .Should().Be(new FixedSessionEffectResult(FixedSessionEffectOutcome.Unconfirmed, 5));
        platform.Events.Count(item => string.Equals(item, "Restart", StringComparison.Ordinal)).Should().Be(1);
        platform.Events.TakeLast(2).Should().Equal("restore", "dispose");
    }

    [Fact]
    public void Native_failure_retains_the_native_error_instead_of_claiming_physical_success()
    {
        var platform = new Platform { NativeOutcome = FixedSessionEffectOutcome.Failed };
        new WindowsFixedSessionEffect(platform).Request(FixedSessionAction.Shutdown)
            .Should().Be(new FixedSessionEffectResult(FixedSessionEffectOutcome.Failed, 123));
        platform.Events.TakeLast(2).Should().Equal("restore", "dispose");
    }

    [Fact]
    public void Throwing_native_request_still_restores_and_disposes_without_replay()
    {
        var platform = new Platform { ThrowOnRequest = true };
        ((Action)(() => new WindowsFixedSessionEffect(platform).Request(FixedSessionAction.Shutdown)))
            .Should().Throw<InvalidOperationException>();
        platform.Events.Should().Equal("current", "duplicate", "enable", "current", "Shutdown", "restore", "dispose");
    }

    private sealed class Platform : IWindowsFixedSessionPlatform
    {
        internal readonly List<string> Events = [];
        internal bool Current { get; set; } = true;
        internal int ActivationError { get; init; }
        internal int RestoreError { get; init; }
        internal bool RetireAfterActivation { get; init; }
        internal bool ThrowOnRequest { get; init; }
        internal FixedSessionEffectOutcome NativeOutcome { get; init; } = FixedSessionEffectOutcome.RequestAccepted;

        public bool IsCurrentInteractiveSession() { Events.Add("current"); return Current; }
        public FixedSessionEffectResult Lock() { Events.Add("lock"); return new(NativeOutcome); }
        public IWindowsShutdownAuthority DuplicateShutdownAuthority() { Events.Add("duplicate"); return new Authority(this); }

        private sealed class Authority(Platform owner) : IWindowsShutdownAuthority
        {
            public int Enable()
            {
                owner.Events.Add("enable");
                if (owner.RetireAfterActivation) { owner.Current = false; }
                return owner.ActivationError;
            }

            public FixedSessionEffectResult Request(FixedSessionAction action)
            {
                owner.Events.Add(action.ToString());
                if (owner.ThrowOnRequest) { throw new InvalidOperationException("Synthetic failure; no real OS call."); }
                return new(owner.NativeOutcome, owner.NativeOutcome == FixedSessionEffectOutcome.Failed ? 123 : 0);
            }

            public int Restore() { owner.Events.Add("restore"); return owner.RestoreError; }
            public void Dispose() => owner.Events.Add("dispose");
        }
    }
}