using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Configuration;

public sealed partial class ResponseModeConfigurationServiceTests
{
    private sealed class ResponseSuggestionClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static ConversationMessage ModeSuggestion(string text = "set responses.default-mode to VoiceOnly",
        RequestOrigin origin = RequestOrigin.LocalUi)
    {
        var request = HostRequest.Create(origin);
        var source = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        return new(request, new(1), 1, 3, DateTimeOffset.UnixEpoch, "Recommend how future responses should appear.",
            ModelTurnOutcome.Succeeded, ModelTurnReason.None)
        {
            Version = 2, Execution = source, ExecutionMetadataRevision = 0, AssistantResponse = new(source, text),
        };
    }

    private static HostActivity ModeInput(ConversationMessage message, RequestOrigin origin = RequestOrigin.LocalUi) =>
        HostActivity.BeginRoot(new(new(Guid.NewGuid()), message.Request.SessionId, new(Guid.NewGuid()), origin),
            HostActivityLayer.Desktop, HostOperation.Request);

    [Theory]
    [InlineData(ResponseOutputMode.Hybrid, false)]
    [InlineData(ResponseOutputMode.VoiceOnly, true)]
    [InlineData(ResponseOutputMode.VisualOnly, false)]
    public async Task ResponseModeSuggestionIsInertThenCommitsExactOneShotAuditedReadback(ResponseOutputMode mode, bool saved)
    {
        await using var f = new Fixture();
        if (saved) { f.Preferences.Mode = ResponseOutputMode.VisualOnly; }
        f.Service.Observe();
        var current = f.Service.Get();
        var message = ModeSuggestion($"set responses.default-mode to {mode}");
        var reads = 0;
        Task Validate(CancellationToken _) { reads++; return Task.CompletedTask; }
        ResponseModeSuggestionReview review;
        using (ModeInput(message))
        {
            review = await ResponseModeSuggestionReview.ReviewAsync(f.Service, f.Policy, new ResponseSuggestionClock(),
                message, () => true, Validate, f.Token);
            review.Description.Should().Contain(saved ? "Current saved: VisualOnly (saved)" : "Current saved: unsaved (default)")
                .And.Contain($"Proposed: {mode}").And.Contain("configured effective:")
                .And.Contain("shared device-local").And.Contain("NEXT eligible response")
                .And.Contain("Mandatory full visual").And.Contain("even in VoiceOnly").And.Contain("Privacy and call");
            f.Service.Get().Should().Be(current);
            f.Store.Tasks.Should().BeEmpty();
            f.Audit.Events.Should().BeEmpty();
            f.Preferences.Writes.Should().Be(0);
            await review.Invoking(item => item.ConfirmAsync(f.Token)).Should().ThrowAsync<InvalidOperationException>();
            review.Available.Should().BeTrue();
        }
        using (review)
        using (ModeInput(message))
        {
            (await review.ConfirmAsync(f.Token, SecurityAuditInitiator.TypedCommand)).Should().BeTrue();
            f.Preferences.Mode.Should().Be(mode);
            f.Preferences.Pending.Should().BeFalse();
            f.Service.Get().Saved.Should().Be(mode);
            f.Service.Get().Effective.Should().Be(mode);
            f.Service.Get().Source.Should().Be("saved");
            review.Available.Should().BeFalse();
            review.Description.Should().BeEmpty();
            await review.Invoking(item => item.ConfirmAsync(f.Token)).Should().ThrowAsync<InvalidOperationException>();
        }
        reads.Should().Be(3);
        f.Preferences.Writes.Should().Be(1);
        f.Audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        f.Audit.Events.Should().OnlyContain(item => item.ActionId == "configuration.response-output");
        f.Playback.SpeechCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("missing-context")]
    [InlineData("foreign-session")]
    [InlineData("invocation")]
    [InlineData("system")]
    [InlineData("same-request")]
    [InlineData("completed")]
    [InlineData("stopped")]
    [InlineData("denied")]
    [InlineData("unavailable")]
    [InlineData("source-before")]
    [InlineData("source-after")]
    [InlineData("settings-after")]
    [InlineData("voice-protected")]
    [InlineData("voice-unknown")]
    [InlineData("foreign-setting")]
    public async Task ResponseModeSuggestionRejectsUntrustedOrStaleReviewWithoutControlIntent(string failure)
    {
        await using var f = new Fixture();
        f.Service.Observe();
        var message = ModeSuggestion(failure is "foreign-setting" ? "set appearance.theme to dark" : "set responses.default-mode to VoiceOnly",
            failure is "voice-protected" or "voice-unknown" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi);
        var request = new HostRequest(new(Guid.NewGuid()), message.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        if (failure is "foreign-session") { request = HostRequest.Create(RequestOrigin.LocalUi); }
        if (failure is "invocation") { request = new(request.RequestId, request.SessionId, request.TaskId, request.Origin, new(Guid.NewGuid())); }
        if (failure is "system") { request = new(request.RequestId, request.SessionId, request.TaskId, RequestOrigin.HostSystem); }
        if (failure is "same-request") { request = message.Request; }
        if (failure is "denied") { message = message with { Outcome = ModelTurnOutcome.Denied, AssistantResponse = null }; }
        if (failure is "unavailable") { f.Service.HoldUnavailable(); }
        if (failure is "voice-protected") { f.Call.State = CallState.Active; }
        if (failure is "voice-unknown") { f.Call.State = CallState.Unknown; }
        using var input = failure is "missing-context" ? null
            : HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        if (failure is "completed") { input!.Complete(HostOperationOutcome.Completed); }
        if (failure is "stopped") { input!.Activity!.Stop(); }
        var live = failure is not "source-before";
        Task Validate(CancellationToken _)
        {
            if (failure is "source-after") { live = false; }
            if (failure is "settings-after") { f.Service.HoldUnavailable(); }
            return Task.CompletedTask;
        }
        await FluentActions.Awaiting(() => ResponseModeSuggestionReview.ReviewAsync(f.Service, f.Policy,
            new ResponseSuggestionClock(), message, () => live, Validate, f.Token)).Should().ThrowAsync<Exception>();
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
        f.Store.Tasks.Should().BeEmpty();
    }

    [Theory]
    [InlineData("expiry")]
    [InlineData("cancel")]
    [InlineData("dispose")]
    [InlineData("source")]
    [InlineData("settings")]
    [InlineData("saved")]
    [InlineData("call")]
    [InlineData("read")]
    [InlineData("generation")]
    [InlineData("revision-during-admission")]
    public async Task ResponseModeSuggestionInvalidationNeverWritesOrRetries(string loss)
    {
        await using var f = new Fixture();
        f.Service.Observe();
        var message = ModeSuggestion();
        var clock = new ResponseSuggestionClock();
        var live = true;
        var confirming = false;
        Task Validate(CancellationToken _)
        {
            if (confirming && loss is "read") { throw new InvalidDataException("Exact retained answer changed."); }
            return Task.CompletedTask;
        }
        ResponseModeSuggestionReview review;
        using (ModeInput(message))
        {
            review = await ResponseModeSuggestionReview.ReviewAsync(f.Service, f.Policy, clock, message, () => live, Validate, f.Token);
        }
        confirming = true;
        if (loss is "expiry") { clock.Now = clock.Now.AddSeconds(60); }
        if (loss is "dispose") { review.Dispose(); }
        if (loss is "source") { live = false; }
        if (loss is "settings") { f.Service.HoldUnavailable(); }
        if (loss is "saved") { f.Preferences.Mode = ResponseOutputMode.VisualOnly; }
        if (loss is "call") { f.Call.State = CallState.Active; }
        if (loss is "generation") { f.Store.BeforeOperation = () => f.Store.Authority = f.Store.Authority! with { Generation = new(2) }; }
        if (loss is "revision-during-admission") { f.Store.BeforeOperation = f.Service.HoldUnavailable; }
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        if (loss is "cancel") { await cancel.CancelAsync(); }
        using (review)
        using (ModeInput(message))
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
    public async Task ResponseModeSuggestionKeepsOriginalChannelInAudioAdmissionAndLockedAudit(
        RequestOrigin source, RequestOrigin reviewOrigin, RequestOrigin confirmOrigin)
    {
        await using var f = new Fixture();
        f.Service.Observe();
        var message = ModeSuggestion(origin: source);
        ResponseModeSuggestionReview review;
        using (ModeInput(message, reviewOrigin))
        {
            review = await ResponseModeSuggestionReview.ReviewAsync(f.Service, f.Policy, new ResponseSuggestionClock(),
                message, () => true, _ => Task.CompletedTask, f.Token);
        }
        using (review)
        using (ModeInput(message, confirmOrigin))
        {
            (await review.ConfirmAsync(f.Token)).Should().BeTrue();
        }
        var voice = source == RequestOrigin.ActivatedVoice || reviewOrigin == RequestOrigin.ActivatedVoice || confirmOrigin == RequestOrigin.ActivatedVoice;
        f.Store.Tasks.Should().OnlyContain(item => item.Request.Origin == (voice ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi));
        f.Audit.Events.Should().OnlyContain(item => item.Initiator == (voice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser));
    }

    [Theory]
    [InlineData("invalid-initiator")]
    [InlineData("voice-initiator-ui")]
    [InlineData("source-request")]
    [InlineData("first-source")]
    [InlineData("second-source")]
    [InlineData("second-cancel")]
    [InlineData("second-settings")]
    [InlineData("refresh-provenance")]
    [InlineData("audit-denied")]
    [InlineData("confirmation-completed")]
    public async Task ResponseModeSuggestionRevalidatesSourceAcrossAdmissionAndImmediatelyBeforeCommit(string failure)
    {
        await using var f = new Fixture();
        f.Service.Observe();
        var message = ModeSuggestion();
        var live = true;
        var reads = 0;
        Task Validate(CancellationToken _)
        {
            reads++;
            if (reads == 2 && failure is "first-source" || reads == 3 && failure is "second-source") { live = false; }
            if (reads == 3 && failure is "second-settings") { f.Service.HoldUnavailable(); }
            if (reads == 3 && failure is "second-cancel") { throw new OperationCanceledException("Source validation cancelled after audio admission."); }
            return Task.CompletedTask;
        }
        ResponseModeSuggestionReview review;
        using (ModeInput(message))
        {
            review = await ResponseModeSuggestionReview.ReviewAsync(f.Service, f.Policy, new ResponseSuggestionClock(),
                message, () => live, Validate, f.Token);
        }
        using (review)
        using (var input = failure is "source-request"
            ? HostActivity.BeginRoot(message.Request, HostActivityLayer.Desktop, HostOperation.Request) : ModeInput(message))
        {
            f.Store.AfterOperation = () =>
            {
                if (failure is "refresh-provenance") { f.Preferences.Mode = ResponseOutputMode.Hybrid; f.Service.Observe(); }
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
                var initiator = failure switch
                {
                    "invalid-initiator" => SecurityAuditInitiator.ModelSuggestion,
                    "voice-initiator-ui" => SecurityAuditInitiator.VoiceCommand,
                    _ => SecurityAuditInitiator.LocalUser,
                };
                if (failure is "second-cancel")
                { await review.Invoking(item => item.ConfirmAsync(f.Token, initiator)).Should().ThrowAsync<OperationCanceledException>(); }
                else
                { await review.Invoking(item => item.ConfirmAsync(f.Token, initiator)).Should().ThrowAsync<InvalidOperationException>(); }
            }
        }
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData("write")]
    [InlineData("audit")]
    [InlineData("terminal")]
    [InlineData("readback")]
    public async Task ResponseModeSuggestionReportsUncertainPersistenceWithoutRollbackOrSecondWrite(string failure)
    {
        await using var f = new Fixture();
        f.Service.Observe();
        var message = ModeSuggestion();
        ResponseModeSuggestionReview review;
        using (ModeInput(message))
        {
            review = await ResponseModeSuggestionReview.ReviewAsync(f.Service, f.Policy, new ResponseSuggestionClock(),
                message, () => true, _ => Task.CompletedTask, f.Token);
        }
        f.Preferences.SaveFailure = failure is "write" ? new IOException() : null;
        f.Preferences.AfterWrite = () => { if (failure is "readback") { f.Preferences.Mode = ResponseOutputMode.Hybrid; } };
        f.Audit.BeforeWrite = item => { if (failure is "audit" && item.Outcome == SecurityAuditOutcome.Succeeded) { throw new IOException(); } };
        f.Store.BeforeCommit = item => { if (failure is "terminal" && f.Preferences.Writes != 0 && item.IsTerminal) { throw new IOException(); } };
        using (review)
        using (ModeInput(message))
        {
            if (failure is "readback")
            { await review.Invoking(item => item.ConfirmAsync(f.Token)).Should().ThrowAsync<InvalidDataException>(); }
            else
            { await review.Invoking(item => item.ConfirmAsync(f.Token)).Should().ThrowAsync<IOException>(); }
            await review.Invoking(item => item.ConfirmAsync(f.Token)).Should().ThrowAsync<InvalidOperationException>();
        }
        f.Service.Get().Available.Should().BeFalse();
        f.Preferences.Writes.Should().Be(failure is "write" ? 0 : 1);
        f.Preferences.Pending.Should().BeTrue();
        f.Playback.SpeechCalls.Should().Be(0);
    }

    [Fact]
    public async Task ResponseModeSuggestionRejectsStoppedParentEvenWhenItsChildRestoresAmbientActivity()
    {
        await using var f = new Fixture();
        f.Service.Observe();
        var message = ModeSuggestion();
        using var input = ModeInput(message);
        using var child = new Activity("test.host-child").Start();
        input.Activity!.Stop();
        child.Stop();
        input.Activity.IsStopped.Should().BeTrue();
        HostActivity.RequireCurrent().Should().BeSameAs(input);
        await FluentActions.Awaiting(() => ResponseModeSuggestionReview.ReviewAsync(f.Service, f.Policy,
            new ResponseSuggestionClock(), message, () => true, _ => Task.CompletedTask, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Events.Should().BeEmpty();
        f.Store.Tasks.Should().BeEmpty();
    }
}
