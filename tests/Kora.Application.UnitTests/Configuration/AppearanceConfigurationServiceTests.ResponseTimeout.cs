using AwesomeAssertions;

using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Configuration;

public sealed partial class AppearanceConfigurationServiceTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public async Task ResponseTimeoutReviewPreservesEveryVoiceChannelThroughNativeConfirmation(bool sourceVoice, bool reviewVoice, bool reset)
    {
        using var listener = ListenToSuggestions();
        var f = new Fixture();
        if (reset) { f.Store.Text["response-timeout-seconds.txt"] = "15"; }
        using var policy = new CallCommunicationPolicy(new TimeoutCall());
        var clock = new TimeoutClock();
        var message = Suggestion(reset ? "reset appearance.response-timeout" : "set appearance.response-timeout to 15",
            sourceVoice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
        AppearanceSuggestionReview review;
        using (BeginSuggestionInput(SuggestionInput(message.Request,
            reviewVoice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi)))
        {
            review = await AppearanceSuggestionReview.ReviewAsync(f.Service, policy, clock, message, () => true,
                _ => Task.CompletedTask, TestContext.Current.CancellationToken);
            review.Description.Should().Contain(reset ? "current 15 (saved); proposed 5 seconds" : "current 5 (default); proposed 15 seconds");
            if (reset) { review.Description.Should().Contain("Reset saves").And.Contain("authoritative default"); }
            f.Store.Writes.Should().Be(0);
        }
        using (review)
        using (BeginSuggestionInput(SuggestionInput(message.Request)))
        {
            (await review.ConfirmAsync(TestContext.Current.CancellationToken)).Succeeded.Should().BeTrue();
            review.Available.Should().BeFalse();
        }
        f.Store.Text["response-timeout-seconds.txt"].Should().Be(reset ? "5" : "15");
        f.Service.Get(AppearanceOption.ResponseTimeout).IsSaved.Should().BeTrue();
        f.Audit.Events.Select(item => item.Outcome).Should()
            .Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        f.Audit.Events.Should().OnlyContain(item => item.ActionId == "configuration.response-timeout"
            && item.Initiator == (sourceVoice || reviewVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser));
    }

    [Theory]
    [InlineData("expiry", false)]
    [InlineData("call", false)]
    [InlineData("source", false)]
    [InlineData("source-read", false)]
    [InlineData("requested-audit", false)]
    [InlineData("write", false)]
    [InlineData("readback", false)]
    [InlineData("terminal-audit", false)]
    [InlineData("cancelled", false)]
    [InlineData("invalid-initiator", false)]
    [InlineData("expiry", true)]
    [InlineData("call", true)]
    [InlineData("source", true)]
    [InlineData("source-read", true)]
    [InlineData("requested-audit", true)]
    [InlineData("write", true)]
    [InlineData("readback", true)]
    [InlineData("terminal-audit", true)]
    [InlineData("cancelled", true)]
    [InlineData("invalid-initiator", true)]
    public async Task ResponseTimeoutReviewNeverReportsSuccessAfterRevocationOrUnconfirmedPersistence(string fault, bool reset)
    {
        using var listener = ListenToSuggestions();
        var f = new Fixture();
        if (reset) { f.Store.Text["response-timeout-seconds.txt"] = "15"; }
        var call = new TimeoutCall();
        using var policy = new CallCommunicationPolicy(call);
        var clock = new TimeoutClock();
        var message = Suggestion(reset ? "reset appearance.response-timeout" : "set appearance.response-timeout to 15", RequestOrigin.ActivatedVoice);
        var eligible = true;
        var confirmed = false;
        Task Validate(CancellationToken _)
        {
            if (confirmed && fault is "source-read") { eligible = false; }
            return Task.CompletedTask;
        }
        AppearanceSuggestionReview review;
        using (BeginSuggestionInput(SuggestionInput(message.Request)))
        {
            review = await AppearanceSuggestionReview.ReviewAsync(f.Service, policy, clock, message, () => eligible,
                Validate, TestContext.Current.CancellationToken);
        }
        using (review)
        using (BeginSuggestionInput(SuggestionInput(message.Request)))
        {
            confirmed = true;
            if (fault is "expiry") { clock.Now = clock.Now.AddSeconds(60); }
            if (fault is "call") { call.Set(CallState.Unknown); }
            if (fault is "source") { eligible = false; }
            if (fault is "requested-audit") { f.Audit.BeforeRequested = () => call.Set(CallState.Active); }
            if (fault is "write") { f.Store.Failure = new IOException("fixture save unavailable"); }
            if (fault is "readback") { f.Store.SuppressWrite = true; }
            if (fault is "terminal-audit") { f.Audit.BeforeSuccess = () => throw new IOException("fixture audit unavailable"); }
            var token = fault is "cancelled" ? new CancellationToken(true) : TestContext.Current.CancellationToken;
            var succeeded = false;
            try
            {
                succeeded = (await review.ConfirmAsync(token,
                    fault is "invalid-initiator" ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser)).Succeeded;
            }
            catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException or IOException) { }
            succeeded.Should().BeFalse();
            f.Store.Text.GetValueOrDefault("response-timeout-seconds.txt").Should()
                .Be(fault is "terminal-audit" ? reset ? "5" : "15" : reset ? "15" : null);
            f.Audit.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded
                && !string.Equals(fault, "terminal-audit", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResponseTimeoutVoiceReviewFailsClosedDuringProtectedCall(bool reset)
    {
        using var listener = ListenToSuggestions();
        var f = new Fixture();
        var call = new TimeoutCall();
        call.Set(CallState.Active);
        using var policy = new CallCommunicationPolicy(call);
        var message = Suggestion(reset ? "reset appearance.response-timeout" : "set appearance.response-timeout to 15", RequestOrigin.ActivatedVoice);
        using var input = BeginSuggestionInput(SuggestionInput(message.Request));
        await FluentActions.Awaiting(() => AppearanceSuggestionReview.ReviewAsync(f.Service, policy, new TimeoutClock(),
            message, () => true, _ => Task.CompletedTask, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Store.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("eligibility")]
    [InlineData("call")]
    public async Task ResponseTimeoutCommitLockRejectsMissingOrChangedAuthorityWithoutWriting(string fault)
    {
        using var listener = ListenToSuggestions();
        var f = new Fixture();
        var call = new TimeoutCall();
        using var policy = new CallCommunicationPolicy(call);
        var revision = policy.Current.Revision;
        var before = f.Service.Get(AppearanceOption.ResponseTimeout);
        var proposal = f.Service.Propose(AppearanceOption.ResponseTimeout, new AppearanceValue.Number(15),
            before.Revision, SecurityAuditInitiator.LocalUser,
            fault is "origin" ? null : RequestOrigin.ActivatedVoice, fault is "eligibility" ? null : () => true);
        if (fault is "call") { f.Audit.BeforeRequested = () => call.Set(CallState.Active); }
        await FluentActions.Awaiting(() => f.Service.ApplyAsync(proposal, policy, revision, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Store.Writes.Should().Be(0);
        f.Service.Get(AppearanceOption.ResponseTimeout).Should().Be(before);
        f.Audit.Events.Select(item => item.Outcome).Should()
            .Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Denied);
    }

    [Theory]
    [InlineData(RequestOrigin.HostSystem)]
    [InlineData(RequestOrigin.LocalUi)]
    public async Task ResponseTimeoutReviewRejectsNonUserOrForeignInput(RequestOrigin origin)
    {
        using var listener = ListenToSuggestions();
        var f = new Fixture();
        using var policy = new CallCommunicationPolicy(new TimeoutCall());
        var message = Suggestion("set appearance.response-timeout to 15");
        using var input = BeginSuggestionInput(HostRequest.Create(origin));
        await FluentActions.Awaiting(() => AppearanceSuggestionReview.ReviewAsync(f.Service, policy, new TimeoutClock(),
            message, () => true, _ => Task.CompletedTask, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Store.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData(SecurityAuditInitiator.TypedCommand)]
    [InlineData((SecurityAuditInitiator)999)]
    public async Task OtherAppearanceReviewStillRejectsNonNativeConfirmation(SecurityAuditInitiator initiator)
    {
        using var listener = ListenToSuggestions();
        var f = new Fixture();
        using var policy = new CallCommunicationPolicy(new TimeoutCall());
        var message = Suggestion();
        AppearanceSuggestionReview review;
        using (BeginSuggestionInput(SuggestionInput(message.Request)))
        {
            review = await AppearanceSuggestionReview.ReviewAsync(f.Service, policy, new TimeoutClock(), message,
                () => true, _ => Task.CompletedTask, TestContext.Current.CancellationToken);
        }
        using (review)
        using (BeginSuggestionInput(SuggestionInput(message.Request)))
        {
            await FluentActions.Awaiting(() => review.ConfirmAsync(TestContext.Current.CancellationToken, initiator))
                .Should().ThrowAsync<InvalidOperationException>();
            review.Available.Should().BeTrue();
        }
        f.Store.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
    }

    private sealed class TimeoutClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class TimeoutCall : ICallStateService
    {
        public CallState CurrentState { get; private set; } = CallState.Clear;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged;
        internal void Set(CallState state)
        {
            CurrentState = state;
            StateChanged?.Invoke(this, new(state));
        }
    }
}
