using AwesomeAssertions;
using System.Diagnostics;
using Kora.Application.Communication;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Communication;

[Collection("Host tracing")]
public sealed class CallSpeechReviewTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => string.Equals(source.Name, "Kora.Application", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public CallSpeechReviewTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Theory]
    [InlineData(CallState.Active)]
    [InlineData(CallState.Suspected)]
    [InlineData(CallState.Unknown)]
    public async Task ExactReviewAllowsOnlyOneStartAndLeavesOrdinaryProtectionUnchanged(CallState state)
    {
        using var policy = new CallCommunicationPolicy(new Source(state));
        using var input = Input();
        using var review = Review(policy);
        review.Available.Should().BeTrue();
        review.Request.Should().Be(input.Request);
        using var confirmation = Input(session: input.Request.SessionId);
        using var lease = review.Confirm();
        confirmation.Activity!.Links.Should().Contain(link => link.Context == input.Activity!.Context);
        review.Available.Should().BeFalse();
        lease.Target.Should().Be(review.Target);
        lease.Output.Should().Be(review.Output);
        lease.Text.Should().Be("Exact answer.");
        lease.IsPlaybackCurrent.Should().BeFalse();
        var starts = 0;
        Task Start() { Interlocked.Increment(ref starts); return Task.CompletedTask; }
        await policy.StartSpeech(Start, () => true);
        var attempts = Enumerable.Range(0, 16).Select(_ => Task.Run(
            () => policy.StartReviewedSpeech(lease, lease.Text, Start, () => true), TestContext.Current.CancellationToken));
        await Task.WhenAll(attempts);
        starts.Should().Be(1);
        lease.IsPlaybackCurrent.Should().BeTrue();
        policy.Current.SuppressSpeech.Should().BeTrue();
        policy.Current.Settings.Should().Be(CallAwareSettings.Default);
        policy.Current.Revision.Should().Be(0);
        lease.Dispose();
        lease.IsPlaybackCurrent.Should().BeFalse();
    }

    [Theory]
    [InlineData(RequestOrigin.ActivatedVoice)]
    [InlineData(RequestOrigin.HostSystem)]
    public void OriginalVoiceAndUnknownOriginCannotCreateOrConfirmAReview(RequestOrigin origin)
    {
        using var policy = new CallCommunicationPolicy(new Source(CallState.Active));
        using var input = Input();
        using var review = Review(policy);
        using var foreign = Input(origin, input.Request.SessionId);
        FluentActions.Invoking(() => Review(policy)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => review.Confirm()).Should().Throw<InvalidOperationException>()
            .WithMessage("Call speech exceptions require original local UI input*");
        review.Available.Should().BeTrue("denied foreign input does not relabel or replace the original UI review");
    }

    [Theory]
    [InlineData("same-request")]
    [InlineData("other-session")]
    [InlineData("invocation")]
    [InlineData("stopped")]
    [InlineData("completed")]
    [InlineData("no-host")]
    public void ConfirmationRequiresSeparateLiveOriginalInputInTheExactSession(string change)
    {
        using var policy = new CallCommunicationPolicy(new Source(CallState.Active));
        using var input = Input();
        using var review = Review(policy);
        if (change is "same-request")
        { FluentActions.Invoking(() => review.Confirm()).Should().Throw<InvalidOperationException>(); return; }
        if (change is "no-host")
        {
            using var unrelated = new System.Diagnostics.Activity("unrelated").Start();
            FluentActions.Invoking(() => review.Confirm()).Should().Throw<InvalidOperationException>();
            return;
        }
        var request = new HostRequest(new(Guid.NewGuid()),
            change is "other-session" ? new(Guid.NewGuid()) : input.Request.SessionId,
            new(Guid.NewGuid()), RequestOrigin.LocalUi,
            change is "invocation" ? new HostId<InvocationIdentity>(Guid.NewGuid()) : null);
        using var confirmation = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Presentation);
        if (change is "stopped")
        {
            confirmation.Activity!.Stop();
            Activity.Current = confirmation.Activity;
        }
        if (change is "completed") { confirmation.Complete(HostOperationOutcome.Completed); }
        var assertion = FluentActions.Invoking(() => review.Confirm()).Should().Throw<InvalidOperationException>();
        if (change is "completed" or "invocation")
        { assertion.WithMessage("Call speech exceptions require original local UI input*"); }
        if (change is "stopped") { assertion.WithMessage("A live host-resolved activity is required."); }
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("revoked")]
    [InlineData("revision")]
    [InlineData("disposed")]
    [InlineData("hold")]
    public void ExpiryAndRevocationRetireBothReviewAndAlreadyConfirmedLease(string change)
    {
        var source = new Source(CallState.Active);
        using var policy = new CallCommunicationPolicy(source);
        var time = new Clock();
        var eligible = true;
        using var input = Input();
        using var review = Review(policy, time, () => eligible);
        using var confirmation = Input(session: input.Request.SessionId);
        using var lease = review.Confirm();
        lease.IsCurrent.Should().BeTrue();
        switch (change)
        {
            case "expired": time.Advance(TimeSpan.FromMinutes(2)); break;
            case "revoked": eligible = false; break;
            case "revision": source.Set(CallState.Suspected); break;
            case "disposed": review.Dispose(); break;
            case "hold": policy.HoldManualControlEvidenceUnavailable(); break;
        }
        review.Available.Should().BeFalse();
        lease.IsCurrent.Should().BeFalse();
        lease.TryStart(policy, lease.Text).Should().BeFalse();
        FluentActions.Invoking(() => review.Confirm()).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("wrong-policy")]
    [InlineData("wrong-text")]
    [InlineData("host-gate")]
    [InlineData("disposed-policy")]
    [InlineData("different-request")]
    [InlineData("stopped-confirmation")]
    [InlineData("completed-confirmation")]
    public async Task AFailedStartCannotBeReplayedOrWidened(string change)
    {
        using var policy = new CallCommunicationPolicy(new Source(CallState.Active));
        using var other = new CallCommunicationPolicy(new Source(CallState.Active));
        using var input = Input();
        using var review = Review(policy);
        using var confirmation = Input(session: input.Request.SessionId);
        using var lease = review.Confirm();
        var starts = 0;
        Task Start() { starts++; return Task.CompletedTask; }
        if (change is "disposed-policy") { policy.Dispose(); }
        if (change is "stopped-confirmation") { confirmation.Activity!.Stop(); }
        if (change is "completed-confirmation") { confirmation.Complete(HostOperationOutcome.Completed); }
        using (change is "different-request" ? Input(session: input.Request.SessionId) : null)
        {
            await (change is "wrong-policy" ? other : policy).StartReviewedSpeech(lease,
                change is "wrong-text" ? "Other answer." : lease.Text, Start, () => change is not "host-gate");
        }
        await policy.StartReviewedSpeech(lease, lease.Text, Start, () => true);
        starts.Should().Be(0);
    }

    [Theory]
    [InlineData("empty-target")]
    [InlineData("empty-text")]
    [InlineData("empty-output")]
    [InlineData("system-output")]
    [InlineData("muted-output")]
    [InlineData("clear")]
    [InlineData("invalid-call")]
    [InlineData("revoked")]
    public void IncompleteOrUnknownAuthorityCannotProduceAReview(string invalid)
    {
        using var policy = new CallCommunicationPolicy(new Source(invalid is "clear" ? CallState.Clear
            : invalid is "invalid-call" ? (CallState)99 : CallState.Active));
        using var input = Input();
        var create = () => new CallSpeechReview(policy, new Clock(),
            new(invalid is "empty-target" ? Guid.Empty : Guid.NewGuid(), 1, 0),
            invalid is "empty-text" ? " " : "Exact answer.",
            new(invalid is "empty-output" ? "" : "speaker", "Speaker", invalid is "muted-output", invalid is "system-output"),
            () => invalid is not "revoked");
        create.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ConsumedReviewCannotMintAnotherLeaseAndLeaseRequiresItsOwnLiveRequest()
    {
        using var policy = new CallCommunicationPolicy(new Source(CallState.Active));
        using var input = Input();
        using var review = Review(policy);
        CallSpeechLease lease;
        using (var confirmation = Input(session: input.Request.SessionId))
        {
            lease = review.Confirm();
            FluentActions.Invoking(() => review.Confirm()).Should().Throw<InvalidOperationException>();
        }
        lease.IsCurrent.Should().BeFalse();
        lease.Dispose();
        lease.Dispose();
    }

    private static CallSpeechReview Review(CallCommunicationPolicy policy, TimeProvider? time = null, Func<bool>? eligible = null) =>
        new(policy, time ?? new Clock(), new(Guid.NewGuid(), 1, policy.Current.Revision), "Exact answer.",
            new("speaker", "Speaker"), eligible ?? (() => true));

    private static HostActivity Input(RequestOrigin origin = RequestOrigin.LocalUi, HostId<SessionIdentity>? session = null) =>
        HostActivity.BeginRoot(new(new(Guid.NewGuid()), session ?? new(Guid.NewGuid()), new(Guid.NewGuid()), origin),
            HostActivityLayer.Application, HostOperation.Presentation);

    internal sealed class Clock : TimeProvider
    {
        private readonly List<Timer> timers = [];
        internal DateTimeOffset Now { get; private set; } = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(this, callback, state);
            timer.Change(dueTime, period);
            timers.Add(timer);
            return timer;
        }
        internal void Advance(TimeSpan amount)
        {
            Now += amount;
            foreach (var timer in timers.ToArray()) { timer.Fire(); }
        }
        private sealed class Timer(Clock clock, TimerCallback callback, object? state) : ITimer
        {
            private DateTimeOffset? due;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (period != Timeout.InfiniteTimeSpan) { throw new InvalidOperationException("Only one-shot fixture timers are supported."); }
                due = dueTime == Timeout.InfiniteTimeSpan ? null : clock.Now + dueTime;
                return true;
            }
            internal void Fire()
            {
                if (due is { } deadline && deadline <= clock.Now) { due = null; callback(state); }
            }
            public void Dispose() => due = null;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    private sealed class Source(CallState state) : ICallStateService
    {
        public CallState CurrentState { get; private set; } = state;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged;
        internal void Set(CallState next)
        {
            CurrentState = next;
            StateChanged?.Invoke(this, new(next));
        }
    }
}
