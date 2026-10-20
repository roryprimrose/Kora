using System.Diagnostics;
using System.Text;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class NewRequestComposerTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, RequestOrigin.LocalUi)]
    [InlineData(RequestOrigin.ActivatedVoice, RequestOrigin.ActivatedVoice)]
    [InlineData(RequestOrigin.ActivatedVoice, RequestOrigin.LocalUi)]
    public async Task ExactRequestReviewRetainsVerbatimOnceThroughNativeCanonicalActionWithoutCleansingOrigin(
        RequestOrigin source, RequestOrigin confirmation)
    {
        using var f = new Fixture();
        await f.Composer.Create();
        var time = new RequestTime();
        var service = new SessionRequestCommandService(f.Workspace, f.Conversation, f.Access, time);
        const string body = "  OrIgInAl e\u0301🙂\r\n\tend  ";
        var review = await Review(service, f.Entry!, source, body);
        f.Retained.Should().BeNull();
        review.Body.Should().Be(body);
        review.Origin.Should().Be(source);
        review.ExpiresAt.Should().Be(time.Now.AddSeconds(60));
        var result = await service.ExecuteAsync(Confirm(review), confirmation, static () => true, TestContext.Current.CancellationToken);
        var receipt = result.RequestSubmission!;
        receipt.Message.Request.Origin.Should().Be(source);
        receipt.Message.Request.SessionId.Should().Be(f.Entry!.Authority.SessionId);
        receipt.Message.Body.Should().Be(body);
        receipt.Message.Outcome.Should().Be(ModelTurnOutcome.Unavailable);
        receipt.Message.Reason.Should().Be(ModelTurnReason.UnregisteredModel);
        receipt.Task.State.Should().Be(HostTaskState.Unavailable);
        receipt.Message.Validate();
        Encoding.UTF8.GetString(SessionCommandResult.Serialize(result)).Should().Contain("retained");
        await service.Invoking(value => value.ExecuteAsync(Confirm(review), confirmation, static () => true, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Retentions.Should().Be(1);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("expiry")]
    [InlineData("control")]
    [InlineData("private")]
    [InlineData("ownership")]
    [InlineData("metadata")]
    [InlineData("generation")]
    [InlineData("done")]
    [InlineData("foreign")]
    [InlineData("retire")]
    [InlineData("wrong-review")]
    [InlineData("wrong-session")]
    [InlineData("missing-review")]
    public async Task ConfirmationConsumesReviewBeforeRevalidatingOriginalAuthorityAndExactTarget(string fault)
    {
        using var f = new Fixture();
        await f.Composer.Create();
        var time = new RequestTime();
        var service = new SessionRequestCommandService(f.Workspace, f.Conversation, f.Access, time);
        var source = true;
        var reviewResult = await service.ExecuteAsync(ReviewCommand(f.Entry!, "original"), RequestOrigin.ActivatedVoice,
            () => source, TestContext.Current.CancellationToken);
        var review = reviewResult.RequestReview!;
        var confirm = Confirm(review);
        var entry = f.Entry!;
        if (fault is "source") { source = false; }
        if (fault is "expiry") { time.Now = review.ExpiresAt; }
        if (fault is "control") { f.Access.ControlRevision++; }
        if (fault is "private") { f.Access.CanInspect = false; }
        if (fault is "ownership") { f.Access.CanControl = false; }
        if (fault is "metadata") { f.Entry = entry with { Metadata = entry.Metadata! with { Revision = new(2) } }; }
        if (fault is "generation") { f.Entry = entry with { Authority = entry.Authority with { Generation = new(2) } }; }
        if (fault is "done") { f.Entry = entry with { Authority = entry.Authority with { IsActive = false } }; }
        if (fault is "foreign") { f.Entry = entry with { Authority = entry.Authority with { SessionId = new(Guid.NewGuid()) } }; }
        if (fault is "retire") { service.Retire(); }
        if (fault is "wrong-review") { confirm = confirm with { RequestReviewId = Guid.NewGuid() }; }
        if (fault is "wrong-session") { confirm = confirm with { SessionId = Guid.NewGuid() }; }
        if (fault is "missing-review") { confirm = confirm with { RequestReviewId = null }; }
        await service.Invoking(value => value.ExecuteAsync(confirm, RequestOrigin.LocalUi, static () => true, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        source = true;
        time.Now = DateTimeOffset.UnixEpoch;
        f.Entry = entry;
        f.Access.CanInspect = true;
        f.Access.CanControl = true;
        await service.Invoking(value => value.ExecuteAsync(Confirm(review), RequestOrigin.LocalUi, static () => true, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Retentions.Should().Be(0);
    }

    [Theory]
    [InlineData("missing-body")]
    [InlineData("missing-session")]
    [InlineData("generation")]
    [InlineData("metadata")]
    [InlineData("done")]
    [InlineData("foreign")]
    [InlineData("source")]
    [InlineData("late")]
    [InlineData("replaced")]
    [InlineData("cancelled")]
    public async Task ReviewRejectsInvalidOrRacedAuthorityWithoutRetainingOrDispatching(string fault)
    {
        using var f = new Fixture();
        await f.Composer.Create();
        var service = new SessionRequestCommandService(f.Workspace, f.Conversation, f.Access, new RequestTime());
        var command = ReviewCommand(f.Entry!, "original");
        if (fault is "missing-body") { command = command with { RequestBody = null }; }
        if (fault is "missing-session") { command = command with { SessionId = null }; }
        if (fault is "generation") { command = command with { Generation = 2 }; }
        if (fault is "metadata") { command = command with { MetadataRevision = 2 }; }
        if (fault is "done") { f.Entry = f.Entry! with { Authority = f.Entry!.Authority with { IsActive = false } }; }
        if (fault is "foreign") { f.Entry = f.Entry! with { Authority = f.Entry!.Authority with { SessionId = new(Guid.NewGuid()) } }; }
        var source = fault is not "source";
        f.AfterRead = () =>
        {
            if (fault is "late") { source = false; }
            if (fault is "replaced") { service.Retire(); }
        };
        using var cancelled = new CancellationTokenSource();
        if (fault is "cancelled") { cancelled.Cancel(); }
        await service.Invoking(value => value.ExecuteAsync(command, RequestOrigin.LocalUi, () => source, cancelled.Token))
            .Should().ThrowAsync<Exception>();
        f.Retained.Should().BeNull();
    }

    [Theory]
    [InlineData(SessionCommandOperation.RequestCancel)]
    [InlineData(SessionCommandOperation.Status)]
    public async Task CancellationOrUnsupportedOperationConsumesOnlyExactReviewWithoutSubmission(SessionCommandOperation operation)
    {
        using var f = new Fixture();
        await f.Composer.Create();
        var service = new SessionRequestCommandService(f.Workspace, f.Conversation, f.Access, new RequestTime());
        var review = await Review(service, f.Entry!, RequestOrigin.LocalUi, "original");
        var operationTask = service.ExecuteAsync(Confirm(review) with { Operation = operation }, RequestOrigin.LocalUi,
            static () => true, TestContext.Current.CancellationToken);
        if (operation == SessionCommandOperation.RequestCancel) { (await operationTask).Outcome.Should().Be("cancelled"); }
        else { await this.Invoking(_ => operationTask).Should().ThrowAsync<InvalidOperationException>(); }
        await service.Invoking(value => value.ExecuteAsync(Confirm(review), RequestOrigin.LocalUi, static () => true, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Retained.Should().BeNull();
    }

    [Theory]
    [InlineData("native-relabel")]
    [InlineData("provider")]
    [InlineData("stopped")]
    [InlineData("stopped-current")]
    [InlineData("completed")]
    [InlineData("invocation")]
    public async Task CanonicalActionRejectsInheritedAuthorityThatCannotEstablishFreshOriginalInput(string fault)
    {
        using var f = new Fixture();
        await f.Composer.Create();
        var request = HostRequest.Create(fault is "provider" ? RequestOrigin.HostSystem : RequestOrigin.ActivatedVoice);
        if (fault is "invocation") { request = new(request.RequestId, request.SessionId, request.TaskId, request.Origin, new(Guid.NewGuid())); }
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        if (fault is "stopped") { root.Activity!.Stop(); }
        if (fault is "stopped-current") { root.Activity!.Stop(); Activity.Current = root.Activity; }
        if (fault is "completed") { root.Complete(HostOperationOutcome.Completed); }
        await f.Conversation.Invoking(value => value.SubmitAsync(f.Entry!, "original", static () => true,
            TestContext.Current.CancellationToken, origin: fault is "native-relabel" ? RequestOrigin.LocalUi : request.Origin))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Retained.Should().BeNull();
    }

    [Fact]
    public async Task PostRetentionMetadataChangeIsRevalidatedBeforeModelAdmissionWithoutReplayingIntent()
    {
        using var f = new Fixture();
        await f.Composer.Create();
        var service = new SessionRequestCommandService(f.Workspace, f.Conversation, f.Access, new RequestTime());
        var review = await Review(service, f.Entry!, RequestOrigin.ActivatedVoice, "original");
        f.BeforeRetain = () => f.Entry = f.Entry! with { Metadata = f.Entry!.Metadata! with { Revision = new(2) } };
        await service.Invoking(value => value.ExecuteAsync(Confirm(review), RequestOrigin.LocalUi, static () => true, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        f.Retained!.Outcome.Should().BeNull();
        f.ModelAdmissions.Should().Be(0);
        await service.Invoking(value => value.ExecuteAsync(Confirm(review), RequestOrigin.LocalUi, static () => true, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Retentions.Should().Be(1);
    }

    [Fact]
    public async Task CanonicalSubmissionRechecksAdmissionAfterAsynchronousOriginalSourceValidation()
    {
        using var f = new Fixture();
        await f.Composer.Create();
        var validations = 0;
        var live = true;
        Task Revalidate(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (++validations == 3) { live = false; }
            return Task.CompletedTask;
        }
        await f.Conversation.Invoking(value => value.SubmitAsync(f.Entry!, "original", () => live,
            TestContext.Current.CancellationToken, Revalidate)).Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        validations.Should().Be(4, "the retained submission readback also revalidates the original source without reviving admission");
        f.ModelAdmissions.Should().Be(0);
        f.Retained!.Outcome.Should().Be(ModelTurnOutcome.Denied);
    }

    [Fact]
    public async Task StoppedSourceLosesHostContextAndCannotBeUsedForSubmission()
    {
        using var f = new Fixture();
        await f.Composer.Create();
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request);
        root.Activity!.Stop();
        Activity.Current = root.Activity;
        HostActivity.Current.Should().BeNull();
        root.Activity.IsStopped.Should().BeTrue();
        try
        {
            await f.Conversation.SubmitAsync(f.Entry!, "original", static () => true,
                TestContext.Current.CancellationToken, origin: RequestOrigin.ActivatedVoice);
            Assert.Fail("Stopped original source must be denied.");
        }
        catch (InvalidOperationException exception)
        {
            exception.Message.Should().Contain("live original user authority");
        }
        f.Retained.Should().BeNull();
    }

    [Fact]
    public async Task FailedCanonicalRetentionEndsBothActivitiesAndCannotReplayConsumedReview()
    {
        using var f = new Fixture();
        await f.Composer.Create();
        var service = new SessionRequestCommandService(f.Workspace, f.Conversation, f.Access, new RequestTime());
        var review = await Review(service, f.Entry!, RequestOrigin.ActivatedVoice, "original");
        f.BeforeRetain = () => throw new IOException("required storage/audit unavailable");
        await service.Invoking(value => value.ExecuteAsync(Confirm(review), RequestOrigin.LocalUi, static () => true, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<ConversationSubmissionUnconfirmedException>();
        f.BeforeRetain = null;
        await service.Invoking(value => value.ExecuteAsync(Confirm(review), RequestOrigin.LocalUi, static () => true, TestContext.Current.CancellationToken))
            .Should().ThrowAsync<InvalidOperationException>();
        f.Retained.Should().BeNull();
    }

    [Fact]
    public async Task ReviewAndSeparateSubmissionLinkDeferredCauseWithoutContentInTraceOrBaggage()
    {
        var stopped = new List<Activity>();
        using var tracing = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(tracing);
        using var f = new Fixture();
        await f.Composer.Create();
        var service = new SessionRequestCommandService(f.Workspace, f.Conversation, f.Access, new RequestTime());
        const string marker = "PRIVATE_VOICE_REQUEST_TRACE_MARKER";
        var review = await Review(service, f.Entry!, RequestOrigin.ActivatedVoice, marker);
        var causes = stopped.Select(value => value.Context).ToArray();
        await service.ExecuteAsync(Confirm(review), RequestOrigin.LocalUi, static () => true, TestContext.Current.CancellationToken);
        stopped.Should().Contain(value => value.Links.Any(link => causes.Contains(link.Context)));
        stopped.SelectMany(value => value.TagObjects).Should().NotContain(value => Equals(value.Value, marker));
        stopped.SelectMany(value => value.Baggage).Should().BeEmpty();
        stopped.Should().OnlyContain(value => value.Status != ActivityStatusCode.Unset);
    }

    private static SessionCommand ReviewCommand(SessionWorkspaceEntry target, string body) =>
        new(SessionCommandOperation.RequestReview, target.Authority.SessionId.Value, target.Authority.Generation.Value,
            target.Metadata?.Revision.Value ?? 0) { RequestBody = body };

    private static SessionCommand Confirm(SessionRequestReview review) =>
        new(SessionCommandOperation.RequestConfirm, review.Target.Authority.SessionId.Value) { RequestReviewId = review.Id };

    private static async Task<SessionRequestReview> Review(SessionRequestCommandService service, SessionWorkspaceEntry target,
        RequestOrigin origin, string body) =>
        (await service.ExecuteAsync(ReviewCommand(target, body), origin, static () => true, TestContext.Current.CancellationToken)).RequestReview!;

    private sealed class RequestTime : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
