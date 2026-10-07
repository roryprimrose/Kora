using AwesomeAssertions;
using Kora.Application.Communication;
using Kora.Core.Communication;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Communication;

public sealed class CallCommunicationPolicyTests
{
    [Theory]
    [InlineData(CallState.Unavailable, false)]
    [InlineData(CallState.Clear, false)]
    [InlineData(CallState.Active, true)]
    [InlineData(CallState.Suspected, true)]
    [InlineData(CallState.Unknown, true)]
    [InlineData((CallState)99, true)]
    public void Manual_layer_never_rewrites_automatic_evidence(CallState state, bool protectedAutomatic)
    {
        var automatic = new Automatic(state);
        using var policy = new CallCommunicationPolicy(automatic);
        policy.Current.IsProtected.Should().Be(protectedAutomatic);
        policy.Current.AllowActivation.Should().BeTrue();
        policy.Current.Authorization(true, true).AllowsReusableGrants.Should().Be(!protectedAutomatic);
        policy.SetManual(true, RequestOrigin.LocalUi, policy.Current.Revision, Eligible).Should().Be(CallMutationOutcome.Applied);
        policy.Current.EffectiveState.Should().Be(CallState.Active);
        policy.Current.SuppressSpeech.Should().BeTrue();
        policy.Current.AutomaticState.Should().Be(state);
        policy.SetManual(false, RequestOrigin.LocalUi, policy.Current.Revision, Eligible).Should().Be(CallMutationOutcome.Applied);
        policy.Current.EffectiveState.Should().Be(state);
        policy.Current.IsProtected.Should().Be(protectedAutomatic);
    }

    [Fact]
    public void Automatic_updates_continue_under_manual_layer_and_unchanged_polls_do_not_advance_revision()
    {
        var automatic = new Automatic(CallState.Clear);
        using var policy = new CallCommunicationPolicy(automatic);
        var notifications = 0;
        policy.Changed += (_, _) => notifications++;
        policy.SetManual(true, RequestOrigin.LocalUi, 0, Eligible);
        automatic.Set(CallState.Active);
        automatic.Set(CallState.Active);
        policy.Current.Revision.Should().Be(2);
        policy.SetManual(true, RequestOrigin.LocalUi, 2, Eligible).Should().Be(CallMutationOutcome.Unchanged);
        automatic.Set(CallState.Unknown);
        policy.SetManual(false, RequestOrigin.LocalUi, 3, Eligible);
        notifications.Should().Be(4);
        policy.Current.IsProtected.Should().BeTrue();
        policy.Current.EffectiveState.Should().Be(CallState.Unknown);
    }

    [Theory]
    [InlineData(RequestOrigin.ActivatedVoice, false, CallMutationOutcome.Applied)]
    [InlineData(RequestOrigin.ActivatedVoice, true, CallMutationOutcome.OriginDenied)]
    [InlineData(RequestOrigin.LocalUi, true, CallMutationOutcome.Applied)]
    [InlineData(RequestOrigin.HostSystem, false, CallMutationOutcome.OriginDenied)]
    [InlineData((RequestOrigin)99, false, CallMutationOutcome.OriginDenied)]
    public void Mutations_use_original_origin(RequestOrigin origin, bool protectedCall, CallMutationOutcome expected)
    {
        using var policy = new CallCommunicationPolicy(new Automatic(protectedCall ? CallState.Active : CallState.Clear));
        policy.SetManual(true, origin, 0, Eligible).Should().Be(expected);
    }

    [Fact]
    public void Stale_pending_voice_changes_and_host_loss_fail_without_mutation()
    {
        var automatic = new Automatic(CallState.Unavailable);
        using var policy = new CallCommunicationPolicy(automatic);
        automatic.Set(CallState.Unknown);
        policy.SetManual(false, RequestOrigin.ActivatedVoice, 0, Eligible).Should().Be(CallMutationOutcome.StaleObservation);
        policy.SetManual(true, RequestOrigin.LocalUi, 1, static () => false).Should().Be(CallMutationOutcome.HostUnavailable);
        policy.SetSettings(new(false, false), RequestOrigin.ActivatedVoice, 1, Eligible, static _ => true)
            .Should().Be(CallMutationOutcome.OriginDenied);
        policy.Current.ManualActive.Should().BeFalse();
    }

    [Fact]
    public void Host_observation_is_rechecked_immediately_before_apply()
    {
        var automatic = new Automatic(CallState.Clear);
        using var policy = new CallCommunicationPolicy(automatic);
        policy.SetManual(true, RequestOrigin.LocalUi, 0, () =>
        {
            automatic.Set(CallState.Unknown);
            return true;
        }).Should().Be(CallMutationOutcome.StaleObservation);
        policy.Current.ManualActive.Should().BeFalse();
    }

    [Fact]
    public void Saved_preferences_are_retained_but_new_downgrades_require_unavailable_exact_review()
    {
        using var policy = new CallCommunicationPolicy(new Automatic(CallState.Active));
        var persistCount = 0;
        bool Persist(CallAwareSettings _) { persistCount++; return true; }
        policy.SetSettings(new(false, true), RequestOrigin.LocalUi, 0, Eligible, Persist)
            .Should().Be(CallMutationOutcome.ExactReviewUnavailable);
        policy.SetSettings(new(true, false), RequestOrigin.LocalUi, 0, Eligible, static _ => false)
            .Should().Be(CallMutationOutcome.PersistenceFailed);
        policy.SetSettings(new(true, false), RequestOrigin.LocalUi, 0, Eligible, Persist).Should().Be(CallMutationOutcome.Applied);
        policy.Current.AllowActivation.Should().BeFalse();
        policy.SetSettings(new(true, true), RequestOrigin.LocalUi, 1, Eligible, Persist)
            .Should().Be(CallMutationOutcome.ExactReviewUnavailable);
        policy.SetSettings(new(true, false), RequestOrigin.LocalUi, 1, Eligible, Persist).Should().Be(CallMutationOutcome.Unchanged);
        policy.SetSettings(new(true, false), RequestOrigin.LocalUi, 0, Eligible, Persist).Should().Be(CallMutationOutcome.StaleObservation);
        policy.LoadSettings(new(false, false));
        policy.Current.SuppressSpeech.Should().BeFalse();
        policy.SetSettings(new(true, false), RequestOrigin.LocalUi, policy.Current.Revision, Eligible, Persist)
            .Should().Be(CallMutationOutcome.Applied);
        policy.LoadSettings(new(true, false));
        persistCount.Should().Be(2);
    }

    [Fact]
    public async Task Speech_is_admitted_only_under_current_host_and_call_policy_and_never_replayed()
    {
        var automatic = new Automatic(CallState.Unavailable);
        using var policy = new CallCommunicationPolicy(automatic);
        var starts = 0;
        Task Speak() { starts++; return Task.CompletedTask; }
        await policy.StartSpeech(Speak, Eligible);
        await policy.StartSpeech(Speak, static () => false);
        automatic.Set(CallState.Suspected);
        await policy.StartSpeech(Speak, Eligible);
        automatic.Set(CallState.Clear);
        starts.Should().Be(1);
        await policy.StartSpeech(Speak, Eligible);
        policy.LoadSettings(new(false, true));
        automatic.Set(CallState.Active);
        await policy.StartSpeech(Speak, Eligible);
        starts.Should().Be(3);
    }

    [Fact]
    public async Task Disposal_unsubscribes_and_closes_all_admission_without_persisting_manual_state()
    {
        var automatic = new Automatic(CallState.Clear);
        var policy = new CallCommunicationPolicy(automatic);
        policy.SetManual(true, RequestOrigin.LocalUi, 0, Eligible);
        var captured = automatic.CaptureHandler();
        policy.Dispose();
        policy.Dispose();
        automatic.Set(CallState.Unknown);
        captured(automatic, new(CallState.Unknown));
        policy.Current.AutomaticState.Should().Be(CallState.Clear);
        policy.SetManual(false, RequestOrigin.LocalUi, policy.Current.Revision, Eligible)
            .Should().Be(CallMutationOutcome.HostUnavailable);
        policy.SetSettings(CallAwareSettings.Default, RequestOrigin.LocalUi, policy.Current.Revision, Eligible, static _ => true)
            .Should().Be(CallMutationOutcome.HostUnavailable);
        await policy.StartSpeech(static () => throw new InvalidOperationException("must not start"), Eligible);
        var load = () => policy.LoadSettings(CallAwareSettings.Default);
        load.Should().Throw<ObjectDisposedException>();
        using var restarted = new CallCommunicationPolicy(automatic);
        restarted.Current.ManualActive.Should().BeFalse();
    }

    private static bool Eligible() => true;

    private sealed class Automatic(CallState initial) : ICallStateService
    {
        public event EventHandler<CallStateChangedEventArgs>? StateChanged;
        public CallState CurrentState { get; private set; } = initial;
        public void Set(CallState state)
        {
            CurrentState = state;
            StateChanged?.Invoke(this, new(state));
        }
        public EventHandler<CallStateChangedEventArgs> CaptureHandler() => StateChanged!;
    }
}
