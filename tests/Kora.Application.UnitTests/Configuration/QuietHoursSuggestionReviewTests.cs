using AwesomeAssertions;
using System.Diagnostics;

using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Configuration;

[Collection("Host tracing")]
public sealed class QuietHoursSuggestionReviewTests
{
    [Theory]
    [InlineData("set notices.quiet-hours to Off", "Off", true)]
    [InlineData("set notices.quiet-hours to weekly Mon,Fri 22:00 06:00 Australia/Sydney", "weekly Mon,Fri 22:00 06:00 Australia/Sydney", true)]
    [InlineData("reset notices.quiet-hours", null, true)]
    [InlineData("set notices.quiet-hours to Off", "Off", false)]
    public async Task OriginalAdviceRequiresFullActualScheduleReviewThenIndependentNativeAuditedConfirmation(string answer, string? expected, bool hasSaved)
    {
        await using var f = new QuietHoursConfigurationServiceTests.Fixture();
        f.Preferences.Value = hasSaved ? f.Schedule : null;
        var validations = 0;
        var message = Answer(answer);
        QuietHoursSuggestionReview review;
        using (Input(message))
        {
            review = await QuietHoursSuggestionReview.ReviewAsync(f.Service, f.Policy, message, () => true,
                _ => { validations++; return Task.CompletedTask; }, f.Token);
            review.Description.Should().Contain("Current saved: " + (hasSaved ? f.Schedule.ToString() : "default Off (not saved)"))
                .And.Contain("effective: " + (hasSaved ? f.Schedule : QuietHoursSchedule.Off))
                .And.Contain("Default: Off").And.Contain("future already-consented").And.Contain("listening")
                .And.Contain("speech consent").And.Contain("ordinary requested answers");
            review.Description.Should().Contain(expected ?? "reset to default Off (not saved)");
            f.Preferences.Writes.Should().Be(0);
            f.Audit.Records.Should().BeEmpty();
            await FluentActions.Awaiting(() => review.ConfirmAsync(f.Token)).Should().ThrowAsync<InvalidOperationException>();
        }
        using (review)
        using (Input(message))
        { (await review.ConfirmAsync(f.Token)).Should().BeTrue(); }
        f.Preferences.Value.Should().Be(expected is null ? null : QuietHoursSchedule.Parse(expected));
        f.Preferences.Pending.Should().BeFalse();
        f.CreateService().Get().Effective.Should().Be(expected is null ? QuietHoursSchedule.Off : QuietHoursSchedule.Parse(expected));
        f.Audit.Records.Select(item => (item.ActionId, item.Initiator, item.Outcome)).Should().Equal(
            ("configuration.notices-quiet-hours", SecurityAuditInitiator.LocalUser, SecurityAuditOutcome.Requested),
            ("configuration.notices-quiet-hours", SecurityAuditInitiator.LocalUser, SecurityAuditOutcome.Succeeded));
        validations.Should().Be(3);
        review.Available.Should().BeFalse();
        review.Description.Should().BeEmpty();
    }

    [Theory]
    [InlineData("retired")]
    [InlineData("source")]
    [InlineData("settings")]
    [InlineData("supersede")]
    [InlineData("expiry")]
    [InlineData("rollback")]
    [InlineData("call")]
    [InlineData("unknown-call")]
    [InlineData("saved")]
    [InlineData("source-await")]
    [InlineData("cancel-await")]
    public async Task StaleOrRetiredAdviceNeverWritesOrEmitsSuccessfulAudit(string change)
    {
        await using var f = new QuietHoursConfigurationServiceTests.Fixture();
        var message = Answer();
        var live = true;
        var confirming = false;
        QuietHoursSuggestionReview? review = null;
        Task Validate(CancellationToken _)
        {
            if (confirming && change is "source-await") { live = false; }
            if (confirming && change is "cancel-await") { review!.Dispose(); }
            return Task.CompletedTask;
        }
        using (Input(message))
        { review = await QuietHoursSuggestionReview.ReviewAsync(f.Service, f.Policy, message, () => live, Validate, f.Token); }
        using (review)
        {
            if (change is "retired") { review.Dispose(); }
            if (change is "source") { live = false; }
            if (change is "expiry") { f.Time.Now = f.Time.Now.AddSeconds(60); }
            if (change is "rollback") { f.Time.Now = f.Time.Now.AddSeconds(-1); }
            if (change is "call") { f.Calls.Set(CallState.Active); }
            if (change is "unknown-call") { f.Calls.Set(CallState.Unknown); }
            if (change is "saved") { f.Preferences.Value = f.Schedule; }
            using (Input(message))
            {
                if (change is "settings") { await f.Service.InspectAsync(RequestOrigin.LocalUi, () => true, f.Token); }
                if (change is "supersede") { _ = await f.Review(); }
                confirming = true;
                await FluentActions.Awaiting(() => review.ConfirmAsync(f.Token)).Should().ThrowAsync<InvalidOperationException>();
            }
        }
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Records.Should().BeEmpty();
    }

    [Theory]
    [InlineData("write")]
    [InlineData("readback")]
    [InlineData("terminal-audit")]
    [InlineData("requested-audit")]
    [InlineData("after-write")]
    [InlineData("call-at-audit")]
    public async Task FailedPersistenceAuditAndLateAuthorityNeverReportConfirmedSuccess(string failure)
    {
        await using var f = new QuietHoursConfigurationServiceTests.Fixture();
        var message = Answer();
        var live = true;
        QuietHoursSuggestionReview review;
        using (Input(message))
        { review = await QuietHoursSuggestionReview.ReviewAsync(f.Service, f.Policy, message, () => live, _ => Task.CompletedTask, f.Token); }
        f.Preferences.Failure = failure;
        f.Preferences.AfterWrite = () => { if (failure is "after-write") { live = false; } };
        f.Audit.BeforeWrite = item =>
        {
            if (failure is "call-at-audit" && item.Outcome == SecurityAuditOutcome.Requested) { f.Calls.Set(CallState.Unknown); }
            if (string.Equals(failure, item.Outcome == SecurityAuditOutcome.Requested ? "requested-audit" : "terminal-audit", StringComparison.Ordinal))
            { throw new IOException("injected audit failure"); }
        };
        using (review)
        using (Input(message))
        {
            if (failure is "call-at-audit") { (await review.ConfirmAsync(f.Token)).Should().BeFalse(); }
            else { await FluentActions.Awaiting(() => review.ConfirmAsync(f.Token)).Should().ThrowAsync<Exception>(); }
        }
        f.Audit.Records.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
        if (failure is not "call-at-audit") { f.Service.Get().Effective.Should().BeNull(); }
        if (failure is "call-at-audit") { f.Preferences.Writes.Should().Be(0); }
    }

    [Theory]
    [InlineData("voice")]
    [InlineData("system")]
    [InlineData("foreign-session")]
    [InlineData("same-request")]
    [InlineData("stopped")]
    [InlineData("completed")]
    [InlineData("malformed")]
    [InlineData("other-setting")]
    [InlineData("unavailable")]
    [InlineData("source-changed")]
    [InlineData("cancelled")]
    [InlineData("source-before")]
    [InlineData("late-source")]
    [InlineData("model-invocation")]
    [InlineData("unsuccessful")]
    [InlineData("review-store")]
    public async Task ReviewRejectsUntrustedOrChangedInputBeforeAnyWrite(string failure)
    {
        await using var f = new QuietHoursConfigurationServiceTests.Fixture();
        var message = Answer(failure is "other-setting" ? "set speech.playback-volume to 30" : "set notices.quiet-hours to Off");
        if (failure is "malformed") { message = message with { AssistantResponse = null }; }
        if (failure is "unsuccessful")
        { message = message with { Version = 1, Revision = 2, Outcome = ModelTurnOutcome.Denied, Execution = null, ExecutionMetadataRevision = null, AssistantResponse = null }; }
        if (failure is "unavailable") { f.Preferences.Pending = true; }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        if (failure is "cancelled") { cancellation.Cancel(); }
        var request = new HostRequest(new(Guid.NewGuid()), failure is "foreign-session" ? new(Guid.NewGuid()) : message.Request.SessionId,
            new(Guid.NewGuid()), failure is "voice" ? RequestOrigin.ActivatedVoice : failure is "system" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi);
        if (failure is "same-request") { request = message.Request; }
        if (failure is "model-invocation")
        { request = new(request.RequestId, request.SessionId, request.TaskId, request.Origin, new(Guid.NewGuid())); }
        using var input = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        using var child = failure is "stopped" ? new Activity("test.host-child").Start() : null;
        if (failure is "stopped") { input.Activity!.Stop(); child!.Stop(); }
        if (failure is "completed") { input.Complete(HostOperationOutcome.Completed); }
        var live = failure is not "source-before";
        var reads = 0;
        var operations = 0;
        f.Store.BeforeOperation = () => { if (failure is "review-store" && ++operations == 2) { live = false; } };
        await FluentActions.Awaiting(() => QuietHoursSuggestionReview.ReviewAsync(f.Service, f.Policy, message, () => live,
            _ =>
            {
                reads++;
                if (failure is "source-changed" || failure is "late-source" && reads == 2) { live = false; }
                return Task.CompletedTask;
            }, cancellation.Token))
            .Should().ThrowAsync<Exception>();
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task CancelledConfirmationRetiresTheExactReviewAndCannotRetryOrWrite()
    {
        await using var f = new QuietHoursConfigurationServiceTests.Fixture();
        var message = Answer();
        QuietHoursSuggestionReview review;
        using (Input(message))
        { review = await QuietHoursSuggestionReview.ReviewAsync(f.Service, f.Policy, message, () => true, _ => Task.CompletedTask, f.Token); }
        using (review)
        using (Input(message))
        using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(f.Token))
        {
            cancellation.Cancel();
            await FluentActions.Awaiting(() => review.ConfirmAsync(cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();
            review.Available.Should().BeFalse();
        }
        f.Preferences.Writes.Should().Be(0);
        f.Audit.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task CancellationDuringActualWriteMustJoinAndLeavesUnconfirmedPersistedIntentWithoutSuccess()
    {
        await using var f = new QuietHoursConfigurationServiceTests.Fixture();
        var message = Answer();
        QuietHoursSuggestionReview review;
        using (Input(message))
        { review = await QuietHoursSuggestionReview.ReviewAsync(f.Service, f.Policy, message, () => true, _ => Task.CompletedTask, f.Token); }
        using (review)
        using (Input(message))
        using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(f.Token))
        using (var release = new ManualResetEventSlim())
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Preferences.AfterWrite = () => { entered.TrySetResult(); release.Wait(f.Token); };
            var work = Task.Run(() => review.ConfirmAsync(cancellation.Token), f.Token);
            try
            {
                await entered.Task.WaitAsync(f.Token);
                cancellation.Cancel();
                work.IsCompleted.Should().BeFalse("cancellation is not a joined preference write");
            }
            finally { release.Set(); }
            await FluentActions.Awaiting(() => work).Should().ThrowAsync<InvalidOperationException>();
            f.Preferences.Value.Should().Be(QuietHoursSchedule.Parse("weekly Mon,Fri 22:00 06:00 UTC"));
            f.Preferences.Pending.Should().BeTrue();
            f.Service.Get().Effective.Should().BeNull();
            f.Audit.Records.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
        }
    }

    internal static ConversationMessage Answer(string answer = "set notices.quiet-hours to weekly Mon,Fri 22:00 06:00 UTC")
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var execution = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        return new(request, new(1), 1, 3, DateTimeOffset.UnixEpoch, "Keep routine completion speech quiet during this explicit period.",
            ModelTurnOutcome.Succeeded, ModelTurnReason.None)
        { Version = 2, Execution = execution, ExecutionMetadataRevision = 0, AssistantResponse = new(execution, answer) };
    }

    internal static HostActivity Input(ConversationMessage message) =>
        HostActivity.BeginRoot(new(new(Guid.NewGuid()), message.Request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
}
