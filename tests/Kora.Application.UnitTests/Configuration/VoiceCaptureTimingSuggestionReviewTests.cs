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

[Collection("Host tracing")]
public sealed class VoiceCaptureTimingSuggestionReviewTests : IDisposable
{
    private readonly ConversationalReplyConfigurationServiceTests tracing = new();
    public void Dispose() => tracing.Dispose();

    [Theory]
    [InlineData(VoiceCaptureTiming.CommandStartId, false)]
    [InlineData(VoiceCaptureTiming.ReplyStartId, false)]
    [InlineData(VoiceCaptureTiming.TrailingSilenceId, false)]
    [InlineData(VoiceCaptureTiming.CommandStartId, true)]
    [InlineData(VoiceCaptureTiming.ReplyStartId, true)]
    [InlineData(VoiceCaptureTiming.TrailingSilenceId, true)]
    public async Task OneFieldSetOrResetReviewsFullTupleAndUsesExistingAuditedWriter(string id, bool reset)
    {
        await using var host = new ConversationalReplyConfigurationServiceTests.Fixture();
        var preferences = new VoiceCaptureTimingConfigurationServiceTests.Preferences { Value = new(7, 20, 2) };
        var service = VoiceCaptureTimingConfigurationServiceTests.Create(preferences, host);
        var value = string.Equals(id, VoiceCaptureTiming.ReplyStartId, StringComparison.Ordinal) ? "20.25" : "2.5";
        var message = Answer(reset ? $"reset {id}" : $"set {id} to {value}");
        var before = service.Get();
        var expected = before.Timing!.With(id, reset ? null : value);
        var validations = 0;
        Task Validate(CancellationToken _) { validations++; return Task.CompletedTask; }
        VoiceCaptureTimingSuggestionReview review;
        using (Input(message))
        {
            review = await VoiceCaptureTimingSuggestionReview.ReviewAsync(service, host.Policy, new Clock(),
                message, () => host.Eligible, Validate, TestContext.Current.CancellationToken);
            review.Description.Should().Contain(before.Timing.Describe()).And.Contain(expected.Describe())
                .And.Contain("saved").And.Contain("future eligible captures only").And.Contain("in-flight capture")
                .And.Contain("companion fields").And.Contain("60 seconds");
            review.Available.Should().BeTrue();
            preferences.Writes.Should().Be(0);
            host.Audit.Events.Should().BeEmpty();
        }
        using (review)
        using (Input(message))
        {
            (await review.ConfirmAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
            review.Available.Should().BeFalse();
            review.Description.Should().BeEmpty();
        }
        validations.Should().Be(2);
        service.GetTiming().Should().Be(expected);
        service.Get().Revision.Should().BeGreaterThan(before.Revision);
        service.Get().IsSaved.Should().BeTrue();
        preferences.Pending.Should().BeFalse();
        preferences.Writes.Should().Be(1);
        host.Audit.Events.Select(item => (item.ActionId, item.Initiator, item.Outcome)).Should().Equal(
            ("configuration.voice-capture-timing", SecurityAuditInitiator.LocalUser, SecurityAuditOutcome.Requested),
            ("configuration.voice-capture-timing", SecurityAuditInitiator.LocalUser, SecurityAuditOutcome.Succeeded));
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("settings")]
    [InlineData("settings-value")]
    [InlineData("settings-provenance")]
    [InlineData("source")]
    [InlineData("call")]
    [InlineData("cancel")]
    [InlineData("disposed")]
    [InlineData("foreign-session")]
    [InlineData("voice")]
    [InlineData("model")]
    [InlineData("same-request")]
    [InlineData("source-request")]
    [InlineData("stopped")]
    [InlineData("completed")]
    [InlineData("invocation")]
    public async Task StaleOrNonNativeConfirmationCannotWrite(string boundary)
    {
        await using var host = new ConversationalReplyConfigurationServiceTests.Fixture();
        var preferences = new VoiceCaptureTimingConfigurationServiceTests.Preferences();
        var service = VoiceCaptureTimingConfigurationServiceTests.Create(preferences, host);
        var clock = new Clock();
        var message = Answer();
        VoiceCaptureTimingSuggestionReview review;
        HostRequest reviewRequest;
        using (var input = Input(message))
        {
            reviewRequest = input.Request;
            review = await VoiceCaptureTimingSuggestionReview.ReviewAsync(service, host.Policy, clock, message,
                () => host.Eligible, _ => Task.CompletedTask, TestContext.Current.CancellationToken);
            review.Description.Should().Contain("default, not saved");
        }
        using (review)
        using (var cancellation = new CancellationTokenSource())
        {
            if (boundary is "expired") { clock.Now = clock.Now.AddSeconds(60); }
            if (boundary is "settings") { service.Reload(); }
            if (boundary is "settings-value") { preferences.Value = new(7, 20, 2); service.Reload(); }
            if (boundary is "settings-provenance") { preferences.Value = VoiceCaptureTiming.Default; service.Reload(); }
            if (boundary is "source") { host.Eligible = false; }
            if (boundary is "call") { host.Call.State = CallState.Active; }
            if (boundary is "cancel") { cancellation.Cancel(); }
            if (boundary is "disposed") { review.Dispose(); }
            var request = new HostRequest(new(Guid.NewGuid()),
                boundary is "foreign-session" ? new(Guid.NewGuid()) : message.Request.SessionId,
                new(Guid.NewGuid()), boundary is "voice" ? RequestOrigin.ActivatedVoice : boundary is "model" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi);
            if (boundary is "same-request") { request = reviewRequest; }
            if (boundary is "source-request") { request = message.Request; }
            if (boundary is "invocation") { request = new(request.RequestId, request.SessionId, request.TaskId, request.Origin, new(Guid.NewGuid())); }
            using var input = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
            if (boundary is "stopped") { input.Activity!.Stop(); }
            if (boundary is "completed") { input.Complete(HostOperationOutcome.Completed); }
            await FluentActions.Awaiting(() => review.ConfirmAsync(cancellation.Token)).Should().ThrowAsync<Exception>();
        }
        preferences.Writes.Should().Be(0);
        host.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("before")]
    [InlineData("after")]
    [InlineData("cancel-before")]
    [InlineData("cancel-after")]
    [InlineData("revision")]
    [InlineData("call")]
    [InlineData("unavailable")]
    [InlineData("foreign")]
    [InlineData("voice")]
    [InlineData("same-request")]
    [InlineData("failed-answer")]
    public async Task ReviewRevalidatesSourceAndOriginalNativeInputAcrossAwait(string boundary)
    {
        await using var host = new ConversationalReplyConfigurationServiceTests.Fixture();
        var preferences = new VoiceCaptureTimingConfigurationServiceTests.Preferences();
        var service = VoiceCaptureTimingConfigurationServiceTests.Create(preferences, host);
        var message = Answer(boundary is "foreign" ? "set speech.windows-rate to -1" : "set voice.trailing-silence to 2");
        if (boundary is "failed-answer") { message = message with { Outcome = ModelTurnOutcome.Unavailable, AssistantResponse = null }; }
        using var cancellation = new CancellationTokenSource();
        if (boundary is "before") { host.Eligible = false; }
        if (boundary is "cancel-before") { cancellation.Cancel(); }
        if (boundary is "unavailable") { preferences.LoadFailure = new IOException(); service.Reload(); }
        Task Validate(CancellationToken _)
        {
            if (boundary is "after") { host.Eligible = false; }
            if (boundary is "cancel-after") { cancellation.Cancel(); }
            if (boundary is "revision") { service.Reload(); }
            if (boundary is "call") { host.Call.State = CallState.Active; }
            return Task.CompletedTask;
        }
        using (var input = boundary is "same-request"
            ? HostActivity.BeginRoot(message.Request, HostActivityLayer.Application, HostOperation.Request)
            : Input(message, boundary is "voice" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi))
        {
            await FluentActions.Awaiting(() => VoiceCaptureTimingSuggestionReview.ReviewAsync(service, host.Policy, new Clock(),
                message, () => host.Eligible, Validate, cancellation.Token)).Should().ThrowAsync<Exception>();
        }
        preferences.Writes.Should().Be(0);
        host.Audit.Events.Should().BeEmpty();
    }

    [Theory]
    [InlineData("source")]
    [InlineData("cancel")]
    [InlineData("save")]
    [InlineData("audit")]
    [InlineData("readback")]
    [InlineData("receipt")]
    [InlineData("late-cancel")]
    public async Task ConfirmationFailureNeverPublishesSuccessfulTiming(string boundary)
    {
        await using var host = new ConversationalReplyConfigurationServiceTests.Fixture();
        var preferences = new VoiceCaptureTimingConfigurationServiceTests.Preferences();
        var service = VoiceCaptureTimingConfigurationServiceTests.Create(preferences, host);
        var message = Answer();
        var validatingConfirmation = false;
        using var cancellation = new CancellationTokenSource();
        Task Validate(CancellationToken _)
        {
            if (validatingConfirmation && boundary is "source") { host.Eligible = false; }
            if (validatingConfirmation && boundary is "cancel") { cancellation.Cancel(); }
            return Task.CompletedTask;
        }
        VoiceCaptureTimingSuggestionReview review;
        using (Input(message))
        {
            review = await VoiceCaptureTimingSuggestionReview.ReviewAsync(service, host.Policy, new Clock(), message,
                () => host.Eligible, Validate, cancellation.Token);
        }
        await host.Admission.RunAsync(RequestOrigin.LocalUi, static () => true, (_, _) => true, TestContext.Current.CancellationToken);
        validatingConfirmation = true;
        preferences.SaveFailure = boundary is "save" ? new IOException() : null;
        preferences.AfterSave = () => { if (boundary is "readback") { preferences.Value = VoiceCaptureTiming.Default; } };
        host.Audit.BeforeWrite = item =>
        {
            if (boundary is "audit" && item.Outcome == SecurityAuditOutcome.Succeeded) { throw new IOException(); }
        };
        host.Store.FailTerminal = boundary is "receipt";
        host.Store.BeforeCommit = task =>
        {
            if (boundary is "late-cancel" && task.State == HostTaskState.Succeeded) { cancellation.Cancel(); }
        };
        using (review)
        using (Input(message))
        {
            await FluentActions.Awaiting(() => review.ConfirmAsync(cancellation.Token)).Should().ThrowAsync<Exception>();
            review.Available.Should().BeFalse();
        }
        if (boundary is "source" or "cancel")
        {
            preferences.Writes.Should().Be(0);
            host.Audit.Events.Should().BeEmpty();
        }
        else { service.Get().Timing.Should().BeNull(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OriginalVoiceSourceCannotBypassProtectedCallAndLateRevocationReturnsDenied(bool protectedCall)
    {
        await using var host = new ConversationalReplyConfigurationServiceTests.Fixture();
        var preferences = new VoiceCaptureTimingConfigurationServiceTests.Preferences();
        var service = VoiceCaptureTimingConfigurationServiceTests.Create(preferences, host);
        var message = Answer(origin: RequestOrigin.ActivatedVoice);
        using var input = Input(message);
        if (protectedCall)
        {
            host.Call.State = CallState.Active;
            await FluentActions.Awaiting(() => VoiceCaptureTimingSuggestionReview.ReviewAsync(service, host.Policy, new Clock(),
                message, () => host.Eligible, _ => Task.CompletedTask, TestContext.Current.CancellationToken))
                .Should().ThrowAsync<InvalidOperationException>();
        }
        else
        {
            using var review = await VoiceCaptureTimingSuggestionReview.ReviewAsync(service, host.Policy, new Clock(),
                message, () => host.Eligible, _ => Task.CompletedTask, TestContext.Current.CancellationToken);
            host.Audit.BeforeWrite = item =>
            {
                if (item.Outcome == SecurityAuditOutcome.Requested) { review.Dispose(); }
            };
            using var confirmation = Input(message);
            (await review.ConfirmAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
            host.Audit.Events.Last().Outcome.Should().Be(SecurityAuditOutcome.Denied);
        }
        preferences.Writes.Should().Be(0);
    }

    [Fact]
    public async Task CancellationDuringAtomicWriteWaitsForActualWriteAndNeverClaimsRollbackOrConfirmedTiming()
    {
        await using var host = new ConversationalReplyConfigurationServiceTests.Fixture();
        var preferences = new VoiceCaptureTimingConfigurationServiceTests.Preferences();
        var service = VoiceCaptureTimingConfigurationServiceTests.Create(preferences, host);
        var message = Answer();
        VoiceCaptureTimingSuggestionReview review;
        using (Input(message))
        {
            review = await VoiceCaptureTimingSuggestionReview.ReviewAsync(service, host.Policy, new Clock(), message,
                static () => true, _ => Task.CompletedTask, TestContext.Current.CancellationToken);
        }
        using (review)
        using (Input(message))
        using (var cancellation = new CancellationTokenSource())
        using (var release = new ManualResetEventSlim())
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            preferences.AfterSave = () =>
            {
                entered.SetResult();
                release.Wait(TestContext.Current.CancellationToken);
            };
            var work = Task.Run(() => review.ConfirmAsync(cancellation.Token), TestContext.Current.CancellationToken);
            await entered.Task;
            try
            {
                review.Dispose();
                cancellation.Cancel();
                work.IsCompleted.Should().BeFalse();
            }
            finally { release.Set(); }
            await FluentActions.Awaiting(() => work).Should().ThrowAsync<OperationCanceledException>();
            preferences.Value.Should().Be(new VoiceCaptureTiming(5, 15, 2));
            preferences.Pending.Should().BeTrue();
            service.Get().Timing.Should().BeNull();
            review.Available.Should().BeFalse();
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static ConversationMessage Answer(string text = "set voice.trailing-silence to 2", RequestOrigin origin = RequestOrigin.LocalUi)
    {
        var request = HostRequest.Create(origin);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        return new(request, new(1), 1, 3, DateTimeOffset.UnixEpoch, "Allow a longer pause.",
            ModelTurnOutcome.Succeeded, ModelTurnReason.None)
        { Version = 2, Execution = provenance, ExecutionMetadataRevision = 0, AssistantResponse = new(provenance, text) };
    }

    private static HostActivity Input(ConversationMessage message, RequestOrigin origin = RequestOrigin.LocalUi) =>
        HostActivity.BeginRoot(new(new(Guid.NewGuid()), message.Request.SessionId, new(Guid.NewGuid()), origin),
            HostActivityLayer.Application, HostOperation.Request);
}
