using System.Diagnostics;
using System.Globalization;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Application.Presentation;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Configuration;

public sealed partial class WindowsSpeechRateConfigurationServiceTests
{
    private sealed class RateClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static ConversationMessage RateAnswer(string text = "set speech.windows-rate to -1",
        RequestOrigin origin = RequestOrigin.LocalUi)
    {
        var request = HostRequest.Create(origin);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        return new(request, new(1), 1, 3, DateTimeOffset.UnixEpoch, "Suggest slower Kora speech.",
            ModelTurnOutcome.Succeeded, ModelTurnReason.None)
        { Version = 2, Execution = provenance, ExecutionMetadataRevision = 0, AssistantResponse = new(provenance, text) };
    }

    private static HostActivity RateInput(ConversationMessage message, RequestOrigin origin = RequestOrigin.LocalUi) =>
        HostActivity.BeginRoot(new(new(Guid.NewGuid()), message.Request.SessionId, new(Guid.NewGuid()), origin),
            HostActivityLayer.Application, HostOperation.Request);

    [Fact]
    public async Task StoppedRateInputCannotBeRevivedByRestoringAmbientChildActivity()
    {
        await using var f = new Fixture();
        var message = RateAnswer();
        using var input = RateInput(message);
        using var child = new Activity("test.host-child").Start();
        input.Activity!.Stop();
        child.Stop();
        HostActivity.RequireCurrent().Should().BeSameAs(input);
        await FluentActions.Awaiting(() => WindowsSpeechRateSuggestionReview.ReviewAsync(f.Service, f.Policy,
            new RateClock(), message, () => true, _ => Task.CompletedTask, f.Token)).Should().ThrowAsync<InvalidOperationException>();
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task AudioRateReviewRejectsMissingBusyForeignAndDisposedOwnersWithoutEffects()
    {
        await using var f = new Fixture();
        await using var other = new Fixture();
        f.Speech.Reload();
        f.Service.Observe();
        var message = RateAnswer();
        using var input = RateInput(message);
        using var settings = new AudioPlaybackSettings(new ImmediateDispatcher(), Microsoft.Extensions.Logging.Abstractions.NullLogger<AudioPlaybackSettings>.Instance,
            rate: f.Service);
        using var missing = new AudioPlaybackSettings(new ImmediateDispatcher(), Microsoft.Extensions.Logging.Abstractions.NullLogger<AudioPlaybackSettings>.Instance);
        using var foreign = new AudioPlaybackSettings(new ImmediateDispatcher(), Microsoft.Extensions.Logging.Abstractions.NullLogger<AudioPlaybackSettings>.Instance,
            rate: other.Service);
        using var review = await settings.ReviewRateSuggestionAsync(f.Policy, new RateClock(), message, () => true, _ => Task.CompletedTask, f.Token);
        await FluentActions.Awaiting(() => missing.ReviewRateSuggestionAsync(f.Policy, new RateClock(), message, () => true,
            _ => Task.CompletedTask, f.Token)).Should().ThrowAsync<InvalidOperationException>();
        await missing.Invoking(item => item.ConfirmRateSuggestionAsync(review, SecurityAuditInitiator.LocalUser, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        await foreign.Invoking(item => item.ConfirmRateSuggestionAsync(review, SecurityAuditInitiator.LocalUser, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        var creation = new TaskCompletionSource<Kora.Core.Authorization.WorkSessionAuthorization>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Store.PendingCreation = creation.Task;
        var refresh = settings.ExecuteRateAsync(new(AppearanceCommandOperation.Get), RequestOrigin.LocalUi,
            SecurityAuditInitiator.TypedCommand, f.Policy, () => true, f.Token);
        settings.IsRateControlActive.Should().BeTrue();
        await FluentActions.Awaiting(() => settings.ReviewRateSuggestionAsync(f.Policy, new RateClock(), message, () => true,
            _ => Task.CompletedTask, f.Token)).Should().ThrowAsync<InvalidOperationException>();
        await settings.Invoking(item => item.ConfirmRateSuggestionAsync(review, SecurityAuditInitiator.LocalUser, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        creation.SetResult(f.Store.Authority!);
        await refresh;
        settings.Dispose();
        await FluentActions.Awaiting(() => settings.ReviewRateSuggestionAsync(f.Policy, new RateClock(), message, () => true,
            _ => Task.CompletedTask, f.Token)).Should().ThrowAsync<ObjectDisposedException>();
        await settings.Invoking(item => item.ConfirmRateSuggestionAsync(review, SecurityAuditInitiator.LocalUser, f.Token))
            .Should().ThrowAsync<ObjectDisposedException>();
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
        public Task InvokeAsync(Func<Task> action) => action();
        public void Post(Action action) => action();
    }

    [Theory]
    [InlineData(-10, false, RequestOrigin.LocalUi, RequestOrigin.LocalUi, RequestOrigin.LocalUi)]
    [InlineData(-1, false, RequestOrigin.ActivatedVoice, RequestOrigin.LocalUi, RequestOrigin.LocalUi)]
    [InlineData(0, true, RequestOrigin.LocalUi, RequestOrigin.ActivatedVoice, RequestOrigin.LocalUi)]
    [InlineData(10, true, RequestOrigin.LocalUi, RequestOrigin.LocalUi, RequestOrigin.ActivatedVoice)]
    public async Task RetainedRateReviewIsInertAndOneShotConfirmationUsesExistingWriterAndFreshHostAuthority(
        int rate, bool saved, RequestOrigin source, RequestOrigin reviewOrigin, RequestOrigin confirmOrigin)
    {
        await using var f = new Fixture();
        if (saved) { f.Preferences.Value = new(2); }
        f.Speech.Reload();
        f.Service.Observe();
        var before = f.Service.Get();
        var message = RateAnswer("set speech.windows-rate to " + rate.ToString(CultureInfo.InvariantCulture), source);
        var reads = 0;
        Task Validate(CancellationToken _) { reads++; return Task.CompletedTask; }
        WindowsSpeechRateSuggestionReview review;
        ActivityContext cause;
        var policies = new List<Activity>();
        using var trace = new ActivityListener
        {
            ShouldListenTo = item => item.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = item => { if (item.GetTagItem("kora.source.request.id") is not null) { policies.Add(item); } },
        };
        ActivitySource.AddActivityListener(trace);
        using (var input = RateInput(message, reviewOrigin))
        {
            cause = input.Activity!.Context;
            review = await WindowsSpeechRateSuggestionReview.ReviewAsync(f.Service, f.Policy, new RateClock(),
                message, () => true, Validate, f.Token);
            review.Description.Should().Contain(saved ? "(saved)" : "(default)").And.Contain($"Proposed: {rate}")
                .And.Contain("WindowsNative").And.Contain("Windows native -10 through 10").And.Contain("Kokoro unchanged")
                .And.Contain("NEXT eligible Windows speech").And.Contain("never synthesized or replayed");
            f.Preferences.Writes.Should().Be(0);
            f.Audit.Events.Should().BeEmpty();
            f.Store.Tasks.Should().BeEmpty();
            f.Service.Get().Should().Be(before);
            await review.Invoking(item => item.ConfirmAsync(f.Token)).Should().ThrowAsync<InvalidOperationException>();
            review.Available.Should().BeTrue();
        }
        using (review)
        using (var input = RateInput(message, confirmOrigin))
        {
            f.Audit.BeforeWrite = _ => HostActivity.RequireCurrent().Request.SessionId.Should().Be(f.Store.Authority!.SessionId)
                .And.NotBe(message.Request.SessionId);
            (await review.ConfirmAsync(f.Token, SecurityAuditInitiator.TypedCommand)).Should().BeTrue();
            f.Preferences.Value.Should().Be(new WindowsSpeechRate(rate));
            f.Playback.Rate.Should().Be(new WindowsSpeechRate(rate));
            f.Preferences.Pending.Should().BeFalse();
            f.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
            f.Audit.Events.Should().OnlyContain(item => item.ActionId == "configuration.windows-speech-rate"
                && item.Initiator == (source == RequestOrigin.ActivatedVoice || reviewOrigin == RequestOrigin.ActivatedVoice
                    || confirmOrigin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.TypedCommand));
            var policy = policies.Single();
            policy.ParentSpanId.Should().Be(input.Activity!.SpanId);
            policy.Links.Single().Context.Should().Be(cause);
            policy.GetTagItem("kora.source.request.id").Should().Be(message.Request.RequestId.Value);
            policy.GetTagItem("kora.source.task.id").Should().Be(message.Request.TaskId.Value);
            policy.GetTagItem("kora.source.turn.id").Should().Be(message.Execution!.Turn.Value);
            policy.GetTagItem("kora.source.origin").Should().Be(source.ToString());
            policy.TagObjects.Should().NotContain(item => Equals(item.Value, message.Body) || Equals(item.Value, message.AssistantResponse!.Body));
            policy.Baggage.Should().BeEmpty();
            policy.Status.Should().Be(ActivityStatusCode.Ok);
            review.Available.Should().BeFalse();
            review.Description.Should().BeEmpty();
            await review.Invoking(item => item.ConfirmAsync(f.Token)).Should().ThrowAsync<InvalidOperationException>();
        }
        reads.Should().Be(3);
        f.Preferences.Writes.Should().Be(1);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("missing-context")]
    [InlineData("foreign-session")]
    [InlineData("invocation")]
    [InlineData("system")]
    [InlineData("model-source")]
    [InlineData("same-request")]
    [InlineData("completed")]
    [InlineData("stopped")]
    [InlineData("denied")]
    [InlineData("foreign-setting")]
    [InlineData("noncanonical")]
    [InlineData("unavailable")]
    [InlineData("kokoro")]
    [InlineData("source-before")]
    [InlineData("source-after")]
    [InlineData("settings-after")]
    [InlineData("input-after")]
    [InlineData("cancel-before")]
    [InlineData("cancel-after")]
    [InlineData("voice-protected")]
    [InlineData("voice-unknown")]
    public async Task RetainedRateReviewRejectsInvalidSourceInputSettingProviderAndCallBeforeAnyEffect(string failure)
    {
        await using var f = new Fixture();
        if (failure is "kokoro") { f.Selection.Provider = Kora.Core.Voice.SpeechProviderIds.Kokoro; }
        f.Speech.Reload();
        f.Service.Observe();
        var message = RateAnswer(origin: failure.StartsWith("voice", StringComparison.Ordinal)
            ? RequestOrigin.ActivatedVoice : failure is "model-source" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi);
        if (failure is "foreign-setting") { message = RateAnswer("set speech.playback-volume to 30"); }
        if (failure is "noncanonical") { message = RateAnswer("set speech.windows-rate to -01"); }
        if (failure is "denied") { message = message with { Outcome = ModelTurnOutcome.Denied, AssistantResponse = null }; }
        if (failure is "unavailable") { f.Service.HoldUnavailable(); }
        if (failure is "voice-protected") { f.Call.State = CallState.Active; }
        if (failure is "voice-unknown") { f.Call.State = CallState.Unknown; }
        var request = new HostRequest(new(Guid.NewGuid()), message.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        if (failure is "foreign-session") { request = HostRequest.Create(RequestOrigin.LocalUi); }
        if (failure is "invocation") { request = new(request.RequestId, request.SessionId, request.TaskId, request.Origin, new(Guid.NewGuid())); }
        if (failure is "system") { request = new(request.RequestId, request.SessionId, request.TaskId, RequestOrigin.HostSystem); }
        if (failure is "same-request") { request = message.Request; }
        using var input = failure is "missing-context" ? null : HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        if (failure is "completed") { input!.Complete(HostOperationOutcome.Completed); }
        if (failure is "stopped") { input!.Activity!.Stop(); }
        var live = failure is not "source-before";
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        if (failure is "cancel-before") { await cancel.CancelAsync(); }
        HostActivity? replacement = null;
        ExecutionContext? replacementContext = null;
        Task Validate(CancellationToken _)
        {
            if (failure is "source-after") { live = false; }
            if (failure is "settings-after") { f.Service.HoldUnavailable(); }
            if (failure is "cancel-after") { cancel.Cancel(); }
            if (failure is "input-after") { replacement = RateInput(message); replacementContext = ExecutionContext.Capture(); }
            return Task.CompletedTask;
        }
        try
        {
            await FluentActions.Awaiting(() => WindowsSpeechRateSuggestionReview.ReviewAsync(f.Service, f.Policy,
                new RateClock(), failure is "null" ? null! : message, () => live, Validate, cancel.Token)).Should().ThrowAsync<Exception>();
        }
        finally
        {
            if (replacementContext is not null) { ExecutionContext.Run(replacementContext, _ => replacement!.Dispose(), null); }
        }
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
        f.Store.Tasks.Should().BeEmpty();
    }

    [Theory]
    [InlineData("invalid-initiator")]
    [InlineData("source-request")]
    [InlineData("expired")]
    [InlineData("disposed")]
    [InlineData("source")]
    [InlineData("settings")]
    [InlineData("call")]
    [InlineData("saved")]
    [InlineData("read-failure")]
    [InlineData("first-source")]
    [InlineData("first-input")]
    [InlineData("second-source")]
    [InlineData("second-input")]
    [InlineData("second-settings")]
    [InlineData("cancel-first")]
    [InlineData("cancel-second")]
    [InlineData("revision-before-refresh")]
    [InlineData("revision-after-refresh")]
    [InlineData("refresh-source")]
    [InlineData("refresh-provider")]
    [InlineData("refresh-saved")]
    [InlineData("confirmation-completed")]
    [InlineData("audit-denied")]
    [InlineData("cancel-before")]
    public async Task RateConfirmationRevalidatesEverySourceAndFreshAdmissionFenceWithoutRetargetOrWrite(string failure)
    {
        await using var f = new Fixture();
        f.Speech.Reload();
        f.Service.Observe();
        var message = RateAnswer();
        var clock = new RateClock();
        var live = true;
        var reads = 0;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        HostActivity? replacement = null;
        ExecutionContext? replacementContext = null;
        Task Validate(CancellationToken _)
        {
            reads++;
            if (reads == 2 && failure is "read-failure") { throw new InvalidDataException("exact retained answer changed"); }
            if (reads == 2 && failure is "first-source" || reads == 3 && failure is "second-source") { live = false; }
            if (reads == 2 && failure is "cancel-first" || reads == 3 && failure is "cancel-second") { cancel.Cancel(); }
            if (reads == 2 && failure is "first-input" || reads == 3 && failure is "second-input")
            { replacement = RateInput(message); replacementContext = ExecutionContext.Capture(); }
            if (reads == 3 && failure is "second-settings") { f.Service.HoldUnavailable(); }
            return Task.CompletedTask;
        }
        WindowsSpeechRateSuggestionReview review;
        using (RateInput(message))
        {
            review = await WindowsSpeechRateSuggestionReview.ReviewAsync(f.Service, f.Policy, clock, message, () => live, Validate, f.Token);
        }
        if (failure is "expired") { clock.Now = clock.Now.AddSeconds(60); }
        if (failure is "disposed") { review.Dispose(); }
        if (failure is "source") { live = false; }
        if (failure is "settings") { f.Service.HoldUnavailable(); }
        if (failure is "call") { f.Call.State = CallState.Active; }
        if (failure is "saved") { f.Preferences.Value = new(5); }
        if (failure is "cancel-before") { await cancel.CancelAsync(); }
        using (review)
        using (var input = failure is "source-request"
            ? HostActivity.BeginRoot(message.Request, HostActivityLayer.Application, HostOperation.Request) : RateInput(message))
        {
            if (failure is "revision-before-refresh") { f.Store.BeforeOperation = f.Service.HoldUnavailable; }
            if (failure is "revision-after-refresh" or "refresh-source" or "refresh-provider" or "refresh-saved" or "confirmation-completed")
            {
                f.Store.AfterOperation = () =>
                {
                    f.Store.AfterOperation = null;
                    if (failure is "revision-after-refresh") { f.Service.HoldUnavailable(); }
                    if (failure is "refresh-source") { live = false; }
                    if (failure is "refresh-provider") { f.Selection.Provider = Kora.Core.Voice.SpeechProviderIds.Kokoro; }
                    if (failure is "refresh-saved") { f.Preferences.Value = WindowsSpeechRate.Default; }
                    if (failure is "confirmation-completed") { input.Complete(HostOperationOutcome.Completed); }
                };
            }
            if (failure is "audit-denied")
            {
                f.Audit.BeforeWrite = item => { if (item.Outcome == SecurityAuditOutcome.Requested) { live = false; } };
                (await review.ConfirmAsync(f.Token)).Should().BeFalse();
                f.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);
            }
            else
            {
                try
                {
                    await review.Invoking(item => item.ConfirmAsync(cancel.Token, failure is "invalid-initiator"
                        ? SecurityAuditInitiator.ModelSuggestion : SecurityAuditInitiator.LocalUser)).Should().ThrowAsync<Exception>();
                }
                finally
                {
                    if (replacementContext is not null) { ExecutionContext.Run(replacementContext, _ => replacement!.Dispose(), null); }
                }
            }
        }
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("source")]
    [InlineData("dispose")]
    public async Task RateReviewJoinsLateSourceValidationWithoutRevivingAuthority(string loss)
    {
        await using var f = new Fixture();
        f.Speech.Reload();
        f.Service.Observe();
        var message = RateAnswer();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirming = false;
        var live = true;
        async Task Validate(CancellationToken _)
        {
            if (!confirming) { return; }
            entered.SetResult();
            await release.Task;
        }
        WindowsSpeechRateSuggestionReview review;
        using (RateInput(message))
        { review = await WindowsSpeechRateSuggestionReview.ReviewAsync(f.Service, f.Policy, new RateClock(), message, () => live, Validate, f.Token); }
        using (review)
        using (RateInput(message))
        using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(f.Token))
        {
            confirming = true;
            var work = review.ConfirmAsync(cancel.Token);
            await entered.Task;
            if (loss is "cancel") { await cancel.CancelAsync(); }
            if (loss is "source") { live = false; }
            if (loss is "dispose") { review.Dispose(); }
            work.IsCompleted.Should().BeFalse();
            release.SetResult();
            await FluentActions.Awaiting(() => work).Should().ThrowAsync<Exception>();
        }
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("write")]
    [InlineData("audit")]
    [InlineData("terminal")]
    [InlineData("readback")]
    public async Task RetainedRateWriterFailureIsUnconfirmedNotSuccessRollbackOrRetry(string failure)
    {
        await using var f = new Fixture();
        f.Speech.Reload();
        f.Service.Observe();
        var message = RateAnswer();
        WindowsSpeechRateSuggestionReview review;
        using (RateInput(message))
        { review = await WindowsSpeechRateSuggestionReview.ReviewAsync(f.Service, f.Policy, new RateClock(), message, () => true, _ => Task.CompletedTask, f.Token); }
        if (failure is "write") { f.Preferences.WriteFailure = new IOException("write failed"); }
        if (failure is "audit") { f.Audit.BeforeWrite = item => { if (item.Outcome == SecurityAuditOutcome.Succeeded) { throw new IOException("audit failed"); } }; }
        if (failure is "terminal")
        {
            f.Store.BeforeCommit = record =>
            {
                if (record.IsTerminal && f.Preferences.Writes > 0) { throw new IOException("terminal apply receipt failed"); }
            };
        }
        if (failure is "readback") { f.Preferences.AfterWrite = () => f.Preferences.Value = new(5); }
        using (review)
        using (RateInput(message))
        {
            if (failure is "readback")
            { await review.Invoking(item => item.ConfirmAsync(f.Token)).Should().ThrowAsync<InvalidDataException>(); }
            else
            { await review.Invoking(item => item.ConfirmAsync(f.Token)).Should().ThrowAsync<IOException>(); }
        }
        review.Available.Should().BeFalse();
        f.Service.Get().Available.Should().BeFalse();
        f.Preferences.Pending.Should().BeTrue();
        f.Preferences.Writes.Should().Be(failure is "write" ? 0 : 1);
    }
}
