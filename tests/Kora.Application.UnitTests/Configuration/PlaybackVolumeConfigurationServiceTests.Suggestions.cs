using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Configuration;

public sealed partial class PlaybackVolumeConfigurationServiceTests
{
    private sealed class VolumeSuggestionClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static ConversationMessage VolumeSuggestion(string text = "set speech.playback-volume to 30",
        RequestOrigin origin = RequestOrigin.LocalUi)
    {
        var request = HostRequest.Create(origin);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        return new(request, new(1), 1, 3, DateTimeOffset.UnixEpoch, "Suggest quieter Kora speech.",
            ModelTurnOutcome.Succeeded, ModelTurnReason.None)
        {
            Version = 2, Execution = provenance, ExecutionMetadataRevision = 0, AssistantResponse = new(provenance, text),
        };
    }

    private static HostActivity VolumeInput(ConversationMessage message, RequestOrigin origin = RequestOrigin.LocalUi) =>
        HostActivity.BeginRoot(new(new(Guid.NewGuid()), message.Request.SessionId, new(Guid.NewGuid()), origin),
            HostActivityLayer.Application, HostOperation.Request);

    [Theory]
    [InlineData("missing-context")]
    [InlineData("foreign-session")]
    [InlineData("invocation")]
    [InlineData("system")]
    [InlineData("same-request")]
    [InlineData("completed")]
    [InlineData("denied")]
    [InlineData("unavailable")]
    [InlineData("source-before")]
    [InlineData("source-after")]
    [InlineData("settings-after")]
    [InlineData("input-after")]
    [InlineData("voice-protected")]
    public async Task VolumeSuggestionFailsClosedBeforeReviewForEveryOriginalInputAndSourceFence(string failure)
    {
        await using var f = new Fixture();
        f.Service.Observe();
        var message = VolumeSuggestion(origin: failure is "voice-protected" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
        var request = new HostRequest(new(Guid.NewGuid()), message.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        if (failure is "foreign-session") { request = HostRequest.Create(RequestOrigin.LocalUi); }
        if (failure is "invocation") { request = new(request.RequestId, request.SessionId, request.TaskId, request.Origin, new(Guid.NewGuid())); }
        if (failure is "system") { request = new(request.RequestId, request.SessionId, request.TaskId, RequestOrigin.HostSystem); }
        if (failure is "same-request") { request = message.Request; }
        if (failure is "denied") { message = message with { Outcome = ModelTurnOutcome.Denied, AssistantResponse = null }; }
        if (failure is "unavailable") { f.Service.HoldUnavailable(); }
        if (failure is "voice-protected") { f.Call.State = CallState.Active; }
        using var input = failure is "missing-context" ? null
            : HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        if (failure is "completed") { input!.Complete(HostOperationOutcome.Completed); }
        var live = failure is not "source-before";
        HostActivity? replacement = null;
        ExecutionContext? replacementContext = null;
        Task Validate(CancellationToken _)
        {
            if (failure is "source-after") { live = false; }
            if (failure is "settings-after") { f.Service.HoldUnavailable(); }
            if (failure is "input-after")
            {
                replacement = VolumeInput(message);
                replacementContext = ExecutionContext.Capture();
            }
            return Task.CompletedTask;
        }
        try
        {
            await FluentActions.Awaiting(() => PlaybackVolumeSuggestionReview.ReviewAsync(f.Service, f.Policy,
                new VolumeSuggestionClock(), message, () => live, Validate, f.Token)).Should().ThrowAsync<InvalidOperationException>();
        }
        finally
        {
            if (replacementContext is not null)
            { ExecutionContext.Run(replacementContext, _ => replacement!.Dispose(), null); }
        }
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
        f.Store.Tasks.Should().BeEmpty();
    }

    [Theory]
    [InlineData("invalid-initiator")]
    [InlineData("first-input")]
    [InlineData("second-source")]
    [InlineData("second-input")]
    [InlineData("second-settings")]
    [InlineData("refresh-source")]
    [InlineData("refresh-effective")]
    [InlineData("refresh-provenance")]
    [InlineData("audit-denied")]
    [InlineData("confirmation-completed")]
    public async Task VolumeSuggestionRevalidatesAfterAwaitedAudioAdmissionAndBeforeLockedCommit(string failure)
    {
        await using var f = new Fixture();
        f.Service.Observe();
        var message = VolumeSuggestion();
        var live = true;
        var reads = 0;
        HostActivity? replacement = null;
        ExecutionContext? replacementContext = null;
        Task Validate(CancellationToken _)
        {
            reads++;
            if (reads == 2 && failure is "first-input")
            {
                replacement = VolumeInput(message);
                replacementContext = ExecutionContext.Capture();
            }
            if (reads == 3 && failure is "second-source") { live = false; }
            if (reads == 3 && failure is "second-input")
            {
                replacement = VolumeInput(message);
                replacementContext = ExecutionContext.Capture();
            }
            if (reads == 3 && failure is "second-settings") { f.Service.HoldUnavailable(); }
            return Task.CompletedTask;
        }
        PlaybackVolumeSuggestionReview review;
        using (VolumeInput(message))
        {
            review = await PlaybackVolumeSuggestionReview.ReviewAsync(f.Service, f.Policy, new VolumeSuggestionClock(),
                message, () => live, Validate, f.Token);
        }
        using (review)
        using (var input = VolumeInput(message))
        {
            f.Store.AfterOperation = () =>
            {
                f.Store.AfterOperation = null;
                if (failure is "refresh-source") { live = false; }
                if (failure is "refresh-provenance") { f.Preferences.Value = PlaybackVolume.Default; }
                if (failure is "refresh-effective") { f.Service.HoldUnavailable(); }
                if (failure is "confirmation-completed") { input.Complete(HostOperationOutcome.Completed); }
            };
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
                    await review.Invoking(item => item.ConfirmAsync(f.Token, failure is "invalid-initiator"
                        ? SecurityAuditInitiator.ModelSuggestion : SecurityAuditInitiator.LocalUser))
                        .Should().ThrowAsync<InvalidOperationException>();
                }
                finally
                {
                    if (replacementContext is not null)
                    { ExecutionContext.Run(replacementContext, _ => replacement!.Dispose(), null); }
                }
            }

        }
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Fact]
    public async Task VolumeSuggestionTraceLinksExactSourceAndDeferredReviewWithoutIncomingAuthorityOrContent()
    {
        await using var f = new Fixture();
        f.Preferences.Value = new(0);
        f.Service.Observe();
        var policies = new List<Activity>();
        using var trace = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (activity.GetTagItem("kora.source.request.id") is not null) { policies.Add(activity); }
            },
        };
        ActivitySource.AddActivityListener(trace);
        var message = VolumeSuggestion(origin: RequestOrigin.ActivatedVoice);
        PlaybackVolumeSuggestionReview review;
        ActivityContext reviewed;
        using (var input = VolumeInput(message))
        {
            reviewed = input.Activity!.Context;
            review = await PlaybackVolumeSuggestionReview.ReviewAsync(f.Service, f.Policy, new VolumeSuggestionClock(),
                message, () => true, _ => Task.CompletedTask, f.Token);
            review.Description.Should().Contain("Current desired: 0%; effective: 0% (saved)")
                .And.Contain("Kora volume is zero: no synthesis or automatic playback");
        }
        using (review)
        using (var hostile = new Activity("untrusted correlation").Start())
        using (var input = VolumeInput(message))
        {
            (await review.ConfirmAsync(f.Token)).Should().BeTrue();
            var policy = policies.Single();
            policy.TraceId.Should().Be(input.Activity!.TraceId).And.NotBe(hostile.TraceId);
            policy.ParentSpanId.Should().Be(input.Activity.SpanId);
            policy.Links.Single().Context.Should().Be(reviewed);
            policy.GetTagItem("kora.source.request.id").Should().Be(message.Request.RequestId.Value);
            policy.GetTagItem("kora.source.task.id").Should().Be(message.Request.TaskId.Value);
            policy.GetTagItem("kora.source.turn.id").Should().Be(message.Execution!.Turn.Value);
            policy.GetTagItem("kora.source.origin").Should().Be(nameof(RequestOrigin.ActivatedVoice));
            policy.Status.Should().Be(ActivityStatusCode.Ok);
            policy.TagObjects.Should().NotContain(tag => Equals(tag.Value, message.Body)
                || Equals(tag.Value, message.AssistantResponse!.Body));
            policy.Baggage.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100, false)]
    public async Task VolumeSuggestionReviewIsInertAndOneShotConfirmRequiresFreshInputAndCommittedReadback(int percent, bool saved)
    {
        await using var f = new Fixture();
        if (saved) { f.Preferences.Value = new(60); }
        f.Service.Observe();
        var state = f.Service.Get();
        var message = VolumeSuggestion("set speech.playback-volume to " + percent.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var reads = 0;
        Task Validate(CancellationToken _) { reads++; return Task.CompletedTask; }
        PlaybackVolumeSuggestionReview review;
        ActivityContext reviewContext;
        using (var input = VolumeInput(message))
        {
            reviewContext = input.Activity!.Context;
            review = await PlaybackVolumeSuggestionReview.ReviewAsync(f.Service, f.Policy, new VolumeSuggestionClock(),
                message, () => true, Validate, f.Token);
            review.Description.Should().Contain(saved ? "(saved)" : "(default)").And.Contain($"Proposed: {percent}%")
                .And.Contain("shared device-local").And.Contain("Zero blocks future synthesis");
            f.Preferences.Writes.Should().Be(0);
            f.Audit.Events.Should().BeEmpty();
            f.Store.Tasks.Should().BeEmpty("review must not create audio admission or durable control intents");
            f.Service.Get().Should().Be(state);
            await review.Invoking(item => item.ConfirmAsync(f.Token)).Should().ThrowAsync<InvalidOperationException>();
            review.Available.Should().BeTrue();
        }
        using (review)
        using (VolumeInput(message))
        {
            // Audio admission deliberately creates its own host-owned request/session, not model authority.
            f.Audit.BeforeWrite = _ => HostActivity.RequireCurrent().Request.SessionId.Should().Be(f.Store.Authority!.SessionId);
            (await review.ConfirmAsync(f.Token, SecurityAuditInitiator.TypedCommand)).Should().BeTrue();
            f.Preferences.Value.Should().Be(new PlaybackVolume(percent));
            f.Preferences.Pending.Should().BeFalse();
            f.Service.Get().Should().Match<PlaybackVolumeState>(value => value.Desired == new PlaybackVolume(percent)
                && value.Effective == value.Desired && value.Source == "saved");
            f.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
            review.Available.Should().BeFalse();
            review.Description.Should().BeEmpty();
            await review.Invoking(item => item.ConfirmAsync(f.Token)).Should().ThrowAsync<InvalidOperationException>();
        }
        reviewContext.TraceId.Should().NotBe(default);
        reads.Should().Be(3);
        f.Preferences.Writes.Should().Be(1);
        f.Playback.SpeechCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("set speech.playback-volume to -1")]
    [InlineData("set speech.playback-volume to 101")]
    [InlineData("set speech.playback-volume to 01")]
    [InlineData("set speech.playback-volume to +1")]
    [InlineData("set speech.playback-volume to 1.0")]
    [InlineData("set speech.playback-volume to 30%")]
    [InlineData(" set speech.playback-volume to 30")]
    [InlineData("SET speech.playback-volume to 30")]
    [InlineData("set speech.playback-volume to 30 ")]
    [InlineData("Kora, set speech.playback-volume to 30")]
    [InlineData("set speech.playback-volume to 30\nset speech.playback-volume to 0")]
    [InlineData("set speech.playback-volume to 30 and confirm")]
    [InlineData("I suggest set speech.playback-volume to 30")]
    [InlineData("reset speech.playback-volume")]
    [InlineData("speak quieter")]
    [InlineData("get speech.playback-volume")]
    [InlineData("set speech.windows-rate to 1")]
    [InlineData("set system.volume to 30")]
    public async Task VolumeSuggestionRejectsNoncanonicalMixedRelativeResetAndForeignAnswers(string text)
    {
        await using var f = new Fixture();
        f.Service.Observe();
        var message = VolumeSuggestion(text);
        using var input = VolumeInput(message);
        await FluentActions.Awaiting(() => PlaybackVolumeSuggestionReview.ReviewAsync(f.Service, f.Policy,
            new VolumeSuggestionClock(), message, () => true, _ => Task.CompletedTask, f.Token)).Should().ThrowAsync<Exception>();
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
        f.Store.Tasks.Should().BeEmpty();
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("disposed")]
    [InlineData("source")]
    [InlineData("settings")]
    [InlineData("saved")]
    [InlineData("call")]
    [InlineData("cancel")]
    [InlineData("read")]
    [InlineData("revision-during-admission")]
    public async Task VolumeSuggestionOriginalAuthorityAndSettingsInvalidationNeverWrite(string loss)
    {
        await using var f = new Fixture();
        f.Service.Observe();
        var message = VolumeSuggestion();
        var clock = new VolumeSuggestionClock();
        var eligible = true;
        var confirmation = false;
        Task Validate(CancellationToken _)
        {
            if (confirmation && loss is "read") { throw new InvalidDataException("exact answer changed"); }
            return Task.CompletedTask;
        }
        PlaybackVolumeSuggestionReview review;
        using (VolumeInput(message))
        {
            review = await PlaybackVolumeSuggestionReview.ReviewAsync(f.Service, f.Policy, clock, message,
                () => eligible, Validate, f.Token);
        }
        confirmation = true;
        if (loss is "expired") { clock.Now = clock.Now.AddSeconds(60); }
        if (loss is "disposed") { review.Dispose(); }
        if (loss is "source") { eligible = false; }
        if (loss is "settings") { f.Service.HoldUnavailable(); }
        if (loss is "saved") { f.Preferences.Value = new(50); }
        if (loss is "call") { f.Call.State = CallState.Active; }
        if (loss is "revision-during-admission") { f.Store.BeforeOperation = f.Service.HoldUnavailable; }
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        if (loss is "cancel") { await cancel.CancelAsync(); }
        using (review)
        using (VolumeInput(message))
        {
            await review.Invoking(item => item.ConfirmAsync(cancel.Token)).Should().ThrowAsync<Exception>();
        }
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
        f.Playback.SpeechCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(RequestOrigin.LocalUi, RequestOrigin.LocalUi, RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice, RequestOrigin.LocalUi, RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.LocalUi, RequestOrigin.ActivatedVoice, RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.LocalUi, RequestOrigin.LocalUi, RequestOrigin.ActivatedVoice)]
    public async Task VolumeSuggestionVoiceAtAnyStageRemainsVoiceInFinalLockedWriter(
        RequestOrigin source, RequestOrigin reviewOrigin, RequestOrigin confirmOrigin)
    {
        await using var f = new Fixture();
        f.Service.Observe();
        var message = VolumeSuggestion(origin: source);
        PlaybackVolumeSuggestionReview review;
        using (VolumeInput(message, reviewOrigin))
        {
            review = await PlaybackVolumeSuggestionReview.ReviewAsync(f.Service, f.Policy, new VolumeSuggestionClock(),
                message, () => true, _ => Task.CompletedTask, f.Token);
        }
        using (review)
        using (VolumeInput(message, confirmOrigin))
        {
            (await review.ConfirmAsync(f.Token)).Should().BeTrue();
        }
        f.Audit.Events.Should().OnlyContain(item => item.Initiator ==
            (source == RequestOrigin.ActivatedVoice || reviewOrigin == RequestOrigin.ActivatedVoice || confirmOrigin == RequestOrigin.ActivatedVoice
                ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser));
    }

    [Theory]
    [InlineData("source")]
    [InlineData("cancel")]
    [InlineData("dispose")]
    public async Task VolumeSuggestionLateValidationIsJoinedAndCannotReviveRetiredAuthority(string loss)
    {
        await using var f = new Fixture();
        f.Service.Observe();
        var message = VolumeSuggestion();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirm = false;
        var eligible = true;
        async Task Validate(CancellationToken _)
        {
            if (!confirm) { return; }
            entered.SetResult();
            await release.Task;
        }
        PlaybackVolumeSuggestionReview review;
        using (VolumeInput(message))
        {
            review = await PlaybackVolumeSuggestionReview.ReviewAsync(f.Service, f.Policy, new VolumeSuggestionClock(),
                message, () => eligible, Validate, f.Token);
        }
        using (review)
        using (VolumeInput(message))
        using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(f.Token))
        {
            confirm = true;
            var work = review.ConfirmAsync(cancel.Token);
            await entered.Task;
            if (loss is "cancel") { await cancel.CancelAsync(); }
            if (loss is "source") { eligible = false; }
            if (loss is "dispose") { review.Dispose(); }
            work.IsCompleted.Should().BeFalse("cancellation or retirement is not actual validator quiescence");
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
    public async Task VolumeSuggestionNeverClaimsSuccessAfterUnconfirmedPreferenceOrTerminalEvidence(string failure)
    {
        await using var f = new Fixture();
        f.Service.Observe();
        var message = VolumeSuggestion();
        PlaybackVolumeSuggestionReview review;
        using (VolumeInput(message))
        {
            review = await PlaybackVolumeSuggestionReview.ReviewAsync(f.Service, f.Policy, new VolumeSuggestionClock(),
                message, () => true, _ => Task.CompletedTask, f.Token);
        }
        if (failure is "write") { f.Preferences.WriteFailure = new IOException("write failed"); }
        if (failure is "audit")
        {
            f.Audit.BeforeWrite = item => { if (item.Outcome == SecurityAuditOutcome.Succeeded) { throw new IOException("terminal audit failed"); } };
        }
        if (failure is "terminal") { f.Store.FailTerminal = true; }
        if (failure is "readback") { f.Preferences.AfterWrite = () => f.Preferences.Value = new(50); }
        using (review)
        using (VolumeInput(message))
        {
            if (failure is "readback")
            { await review.Invoking(item => item.ConfirmAsync(f.Token)).Should().ThrowAsync<InvalidDataException>(); }
            else
            { await review.Invoking(item => item.ConfirmAsync(f.Token)).Should().ThrowAsync<IOException>(); }
        }
        review.Available.Should().BeFalse();
        f.Service.Get().Available.Should().BeFalse();
        f.Playback.SpeechCalls.Should().Be(0);
    }
}
