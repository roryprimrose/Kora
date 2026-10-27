using AwesomeAssertions;
using Kora.Application.Configuration.SettingInspection;
using Kora.Core.Auditing;
using Kora.Core.Configuration.SettingInspection;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Configuration.SettingInspection;

[Collection("Host tracing")]
public sealed class SettingInspectionWorkflowTests
{
    [Theory]
    [InlineData(ModelTurnOutcome.Succeeded, SettingInspectionOutcome.Succeeded)]
    [InlineData(ModelTurnOutcome.Unavailable, SettingInspectionOutcome.Unavailable)]
    [InlineData(ModelTurnOutcome.Cancelled, SettingInspectionOutcome.Cancelled)]
    [InlineData(ModelTurnOutcome.Unknown, SettingInspectionOutcome.InvalidResponse)]
    [InlineData(ModelTurnOutcome.Denied, SettingInspectionOutcome.Denied)]
    [InlineData(ModelTurnOutcome.DeniedEgress, SettingInspectionOutcome.Denied)]
    public async Task OriginalFullReviewAndOneUseConsentReturnOnlyExactRealReceipts(ModelTurnOutcome outcome, SettingInspectionOutcome expected)
    {
        await using var f = new SettingInspectionFixture { Outcome = outcome };
        f.Observe();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var review = await f.Workflow.ReviewAsync("What is my reading text size?", () => f.Eligible, f.Token);
        f.Calls.Should().Be(0);
        f.Events.Should().BeEmpty();
        var result = await f.Workflow.AnswerAsync(review, () => f.Eligible, f.Token);
        result.Outcome.Should().Be(expected);
        result.Receipt.Should().NotBeNull();
        f.Calls.Should().Be(1);
        f.Joins.Should().Be(1);
        f.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested,
            expected == SettingInspectionOutcome.Succeeded ? SecurityAuditOutcome.Succeeded
                : expected == SettingInspectionOutcome.Cancelled ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Failed);
        (await f.Workflow.AnswerAsync(review, () => true, f.Token)).Outcome.Should().Be(SettingInspectionOutcome.Denied);
        f.Calls.Should().Be(1);
        f.Store.Writes.Should().Be(0);
        f.Logger.Messages.Should().NotContain(message => message.Contains("private", StringComparison.Ordinal));
        f.Playback.Effects.Should().Be(0);
    }

    [Fact]
    public async Task OriginalVoiceRemainsVoiceClassifiedAndUnsupportedIsGenuine()
    {
        await using var f = new SettingInspectionFixture { Body = SettingInspectionReview.Unsupported };
        f.Observe();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request);
        var review = await f.Workflow.ReviewAsync("Can these settings answer this?", () => true, f.Token);
        var result = await f.Workflow.AnswerAsync(review, () => true, f.Token);
        result.Outcome.Should().Be(SettingInspectionOutcome.Unsupported);
        result.Receipt!.Message.Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
        f.Events.Should().OnlyContain(item => item.Initiator == SecurityAuditInitiator.VoiceCommand);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("foreign")]
    [InlineData("stopped")]
    [InlineData("completed")]
    public async Task NonOriginalConfirmationDoesNotCreateAuditOrConversation(string defect)
    {
        await using var f = new SettingInspectionFixture();
        f.Observe();
        SettingInspectionSession session;
        using (HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request))
        { session = await f.Workflow.ReviewAsync("Why?", () => true, f.Token); }
        using var root = defect is "none" ? null : HostActivity.BeginRoot(defect is "foreign"
            ? HostRequest.Create(RequestOrigin.LocalUi) : session.Review.Original, HostActivityLayer.Application, HostOperation.Request);
        if (defect is "stopped") { root!.Activity!.Stop(); }
        if (defect is "completed") { root!.Complete(HostOperationOutcome.Completed); }
        (await f.Workflow.AnswerAsync(session, () => true, f.Token)).Outcome.Should().Be(SettingInspectionOutcome.Denied);
        f.Events.Should().BeEmpty();
        f.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("control")]
    [InlineData("inspect")]
    [InlineData("revision")]
    [InlineData("input")]
    [InlineData("source")]
    [InlineData("expiry")]
    [InlineData("fault")]
    [InlineData("session")]
    [InlineData("body")]
    [InlineData("task")]
    [InlineData("terminal")]
    [InlineData("state")]
    public async Task ChangesAcrossAwaitOrUncertainReceiptNeverClaimLateSuccess(string defect)
    {
        await using var f = new SettingInspectionFixture { Defect = defect };
        f.Observe();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var review = await f.Workflow.ReviewAsync("What is confirmed?", () => f.Eligible, f.Token);
        f.BeforeReturn = () =>
        {
            switch (defect)
            {
                case "control": f.CanControl = false; break;
                case "inspect": f.CanInspect = false; break;
                case "revision": f.ControlRevision++; break;
                case "input": f.Eligible = false; break;
                case "source": f.Store.Text["appearance-theme.txt"] = "Dark"; break;
                case "expiry": f.Time.Now = review.Review.ExpiresAt; break;
                case "fault": throw new IOException("private question not for logs");
            }
        };
        var result = await f.Workflow.AnswerAsync(review, () => true, f.Token);
        result.Outcome.Should().Be(defect is "fault" or "session" or "body" or "task" or "terminal" or "state"
            ? SettingInspectionOutcome.Unconfirmed : defect is "source" ? SettingInspectionOutcome.Unavailable : SettingInspectionOutcome.Denied);
        result.Receipt.Should().BeNull();
        f.Joins.Should().Be(1);
        f.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
        f.Store.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationWaitsForIgnoringSourceOrModelAndPhysicalJoin(bool source)
    {
        await using var f = new SettingInspectionFixture();
        f.Observe();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var review = await f.Workflow.ReviewAsync("Why?", () => true, f.Token);
        using var cancellation = new CancellationTokenSource();
        using var releaseSource = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseModel = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseJoin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (source)
        {
            f.Store.Reading = _ => { entered.TrySetResult(); releaseSource.Wait(f.Token); };
        }
        else
        {
            f.Hold = async () => { entered.TrySetResult(); await releaseModel.Task; };
            f.Join = () => releaseJoin.Task;
        }
        var work = f.Workflow.AnswerAsync(review, () => true, cancellation.Token);
        await entered.Task.WaitAsync(f.Token);
        cancellation.Cancel();
        work.IsCompleted.Should().BeFalse();
        releaseSource.Set();
        releaseModel.SetResult();
        if (!source)
        {
            await f.JoinEntered.Task.WaitAsync(f.Token);
            work.IsCompleted.Should().BeFalse();
            releaseJoin.SetResult();
        }
        var result = await work;
        result.Outcome.Should().Be(SettingInspectionOutcome.Cancelled);
        result.Receipt.Should().BeNull();
        f.Calls.Should().Be(source ? 0 : 1);
        f.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData(SecurityAuditOutcome.Requested)]
    [InlineData(SecurityAuditOutcome.Succeeded)]
    public async Task AuditUncertaintyIsNeverSuccessAndNeverRetries(SecurityAuditOutcome failure)
    {
        await using var f = new SettingInspectionFixture { FailAudit = failure };
        f.Logger.Enabled = false;
        f.Observe();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var review = await f.Workflow.ReviewAsync("Why?", () => true, f.Token);
        if (failure == SecurityAuditOutcome.Succeeded)
        {
            await FluentActions.Awaiting(() => f.Workflow.AnswerAsync(review, () => true, f.Token)).Should().ThrowAsync<IOException>();
        }
        else
        { (await f.Workflow.AnswerAsync(review, () => true, f.Token)).Outcome.Should().Be(SettingInspectionOutcome.Unconfirmed); }
        (await f.Workflow.AnswerAsync(review, () => true, f.Token)).Outcome.Should().Be(SettingInspectionOutcome.Denied);
        f.Calls.Should().Be(failure == SecurityAuditOutcome.Succeeded ? 1 : 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RevocationDuringFinalPhysicalJoinCannotPublishSuccessAudit(bool source)
    {
        await using var f = new SettingInspectionFixture();
        f.Observe();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var review = await f.Workflow.ReviewAsync("Why?", () => f.Eligible, f.Token);
        f.Join = () =>
        {
            if (source) { f.Store.Text["appearance-theme.txt"] = "Dark"; }
            else { f.Eligible = false; }
            return Task.CompletedTask;
        };
        var result = await f.Workflow.AnswerAsync(review, () => true, f.Token);
        result.Outcome.Should().Be(source ? SettingInspectionOutcome.Unavailable : SettingInspectionOutcome.Denied);
        result.Receipt.Should().BeNull();
        f.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("question")]
    [InlineData("late-host")]
    [InlineData("expiry")]
    public async Task ReviewCannotReadBeforeAdmissionOrRenewAnOverlongCapture(string defect)
    {
        await using var f = new SettingInspectionFixture();
        f.Observe();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        if (defect is "host") { f.CanControl = false; }
        if (defect is "late-host") { f.Store.Reading = _ => f.Eligible = false; }
        if (defect is "expiry") { f.Store.Reading = _ => f.Time.Now = DateTimeOffset.UnixEpoch.AddMinutes(3); }
        await FluentActions.Awaiting(() => f.Workflow.ReviewAsync(defect is "question" ? new string('x', 257) : "Why?",
            () => f.Eligible, f.Token)).Should().ThrowAsync<Exception>();
        if (defect is "host" or "question") { f.Store.Reads.Should().BeEmpty(); }
        f.Calls.Should().Be(0);
        f.Events.Should().BeEmpty();
    }
}
