using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Communication;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Communication;

[Collection("Host tracing")]
public sealed class ManualCallControlTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };
    public ManualCallControlTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();
    private static ManualCallRetirement Retire() => new(Task.CompletedTask, Eligible);
    private static bool Eligible() => true;

    [Theory]
    [InlineData(CallState.Unavailable)]
    [InlineData(CallState.Active)]
    [InlineData(CallState.Unknown)]
    public async Task Shared_control_commits_original_session_intent_and_preserves_automatic_state_and_saved_flags(CallState automatic)
    {
        var store = new AudioControlTestStore();
        await using var control = new ManualCallControl(store, store, new(store));
        using var policy = new CallCommunicationPolicy(new Automatic(automatic));
        policy.LoadSettings(new(true, false));
        var retirements = 0;
        ManualCallRetirement Retirement() { retirements++; return new(Task.CompletedTask, Eligible); }
        (await control.SetAsync(true, RequestOrigin.LocalUi, policy.Current.Revision, policy, Eligible, Retirement, CancellationToken.None))
            .Should().Be(CallMutationOutcome.Applied);
        (await control.SetAsync(true, RequestOrigin.LocalUi, policy.Current.Revision, policy, Eligible, Retirement, CancellationToken.None))
            .Should().Be(CallMutationOutcome.Unchanged);
        (await control.SetAsync(false, RequestOrigin.LocalUi, policy.Current.Revision, policy, Eligible, Retirement, CancellationToken.None))
            .Should().Be(CallMutationOutcome.Applied);
        policy.Current.AutomaticState.Should().Be(automatic);
        policy.Current.Settings.Should().Be(new CallAwareSettings(true, false));
        retirements.Should().Be(2);
        store.Tasks.Where(task => task.IsTerminal).Should().OnlyContain(task => task.State == HostTaskState.Succeeded);
        store.Tasks.Select(task => task.Request.SessionId).Distinct().Should().ContainSingle();
        using var restart = new CallCommunicationPolicy(new Automatic(automatic));
        restart.Current.ManualActive.Should().BeFalse();
    }

    [Fact]
    public async Task Voice_original_origin_is_not_relabelled_by_ambient_UI_input_and_stale_call_revision_is_denied()
    {
        var store = new AudioControlTestStore();
        await using var control = new ManualCallControl(store, store, new(store));
        var automatic = new Automatic(CallState.Clear);
        using var policy = new CallCommunicationPolicy(automatic);
        using var original = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request);
        (await control.SetAsync(true, RequestOrigin.LocalUi, 0, policy, Eligible, Retire, CancellationToken.None)).Should().Be(CallMutationOutcome.Applied);
        (await control.SetAsync(false, RequestOrigin.LocalUi, 1, policy, Eligible, Retire, CancellationToken.None)).Should().Be(CallMutationOutcome.OriginDenied);
        store.LastRequest!.Origin.Should().Be(RequestOrigin.ActivatedVoice);
        automatic.Set(CallState.Unknown);
        (await control.SetAsync(false, RequestOrigin.LocalUi, 1, policy, Eligible, Retire, CancellationToken.None)).Should().Be(CallMutationOutcome.StaleObservation);
        policy.Current.ManualActive.Should().BeTrue();
        store.Tasks.Last().State.Should().Be(HostTaskState.Denied);
    }

    [Theory]
    [InlineData(RequestOrigin.HostSystem)]
    [InlineData((RequestOrigin)99)]
    public async Task Unknown_provenance_and_unknown_host_never_create_authority(RequestOrigin origin)
    {
        var store = new AudioControlTestStore();
        await using var control = new ManualCallControl(store, store, new(store));
        using var policy = new CallCommunicationPolicy(new Automatic(CallState.Unavailable));
        var denied = () => control.SetAsync(true, origin, 0, policy, Eligible, Retire, CancellationToken.None);
        await denied.Should().ThrowAsync<InvalidOperationException>();
        (await control.SetAsync(true, RequestOrigin.LocalUi, 0, policy, static () => false, Retire, CancellationToken.None))
            .Should().Be(CallMutationOutcome.HostUnavailable);
        store.Tasks.Should().BeEmpty();
    }

    [Theory]
    [InlineData("creation")]
    [InlineData("creation-intent")]
    [InlineData("operation-intent")]
    [InlineData("creation-receipt")]
    [InlineData("requested")]
    [InlineData("outcome")]
    [InlineData("retirement")]
    [InlineData("receipt")]
    [InlineData("generation")]
    [InlineData("context")]
    [InlineData("stopped")]
    [InlineData("owner")]
    [InlineData("cancel")]
    public async Task Required_audit_generation_owner_cancellation_and_lost_receipts_do_not_retry_or_claim_rollback(string failure)
    {
        var store = new AudioControlTestStore();
        var control = new ManualCallControl(store, store, new(store));
        await using var owned = control;
        using var policy = new CallCommunicationPolicy(new Automatic(CallState.Unavailable));
        using var cancellation = new CancellationTokenSource();
        var eligible = true;
        var intents = 0;
        store.BeforeControlIntent = _ =>
        {
            intents++;
            if (failure is "creation-intent" && intents == 1 || failure is "operation-intent" && intents == 2)
            {
                throw new IOException("intent receipt unavailable");
            }
        };
        if (failure is "creation") { store.CreateFailure = new IOException("unknown creation"); }
        if (failure is "creation-receipt") { store.FailTerminal = true; }
        if (failure is "requested") { store.FailRequestedAudit = true; }
        if (failure is "outcome") { store.FailOutcomeAudit = true; }
        store.BeforeOperation = () =>
        {
            if (failure is "generation") { store.Authority = store.Authority! with { Generation = new(2) }; }
            if (failure is "context") { store.ForeignManualContext = true; }
            if (failure is "stopped") { HostActivity.RequireCurrent().Activity!.Stop(); }
            if (failure is "owner") { eligible = false; }
            if (failure is "cancel") { cancellation.Cancel(); }
            if (failure is "receipt") { store.FailTerminal = true; }
        };
        ManualCallRetirement Retirement() => new(failure is "retirement" ? Task.FromException(new IOException("unconfirmed closure")) : Task.CompletedTask, Eligible);
        var run = () => control.SetAsync(true, RequestOrigin.LocalUi, 0, policy, () => eligible, Retirement, cancellation.Token);
        await run.Should().ThrowAsync<Exception>();
        policy.Current.ManualActive.Should().Be(failure is "outcome" or "retirement" or "receipt");
        store.CreateFailure = null;
        store.FailTerminal = false;
        store.FailRequestedAudit = false;
        store.FailOutcomeAudit = false;
        eligible = true;
        (await control.SetAsync(false, RequestOrigin.LocalUi, policy.Current.Revision, policy, Eligible, Retire, CancellationToken.None))
            .Should().Be(CallMutationOutcome.HostUnavailable);
    }

    [Fact]
    public async Task Disposal_and_pre_cancelled_input_cannot_create_or_restore_manual_state()
    {
        var store = new AudioControlTestStore();
        var control = new ManualCallControl(store, store, new(store));
        using var policy = new CallCommunicationPolicy(new Automatic(CallState.Unavailable));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = () => control.SetAsync(true, RequestOrigin.LocalUi, 0, policy, Eligible, Retire, cancellation.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        await control.DisposeAsync();
        await control.DisposeAsync();
        var closed = () => control.SetAsync(true, RequestOrigin.LocalUi, 0, policy, Eligible, Retire, CancellationToken.None);
        await closed.Should().ThrowAsync<ObjectDisposedException>();
        store.Tasks.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, CallMutationOutcome.HostUnavailable)]
    [InlineData(true, CallMutationOutcome.StaleObservation)]
    public async Task Source_and_native_admission_are_rechecked_after_the_owned_retirement_fence(bool sourceChanged, CallMutationOutcome expected)
    {
        var store = new AudioControlTestStore();
        await using var control = new ManualCallControl(store, store, new(store));
        var automatic = new Automatic(CallState.Clear);
        using var policy = new CallCommunicationPolicy(automatic);
        ManualCallRetirement Retirement()
        {
            if (sourceChanged) { automatic.Set(CallState.Unknown); }
            return new(Task.CompletedTask, () => sourceChanged);
        }
        (await control.SetAsync(true, RequestOrigin.LocalUi, 0, policy, Eligible, Retirement, CancellationToken.None))
            .Should().Be(expected);
        policy.Current.ManualActive.Should().BeFalse();
        policy.Current.AutomaticState.Should().Be(sourceChanged ? CallState.Unknown : CallState.Clear);
        store.Tasks.Last().State.Should().Be(HostTaskState.Denied);
    }

    private sealed class Automatic(CallState initial) : ICallStateService
    {
        public CallState CurrentState { get; private set; } = initial;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged;
        public void Set(CallState state) { CurrentState = state; StateChanged?.Invoke(this, new(state)); }
    }
}
