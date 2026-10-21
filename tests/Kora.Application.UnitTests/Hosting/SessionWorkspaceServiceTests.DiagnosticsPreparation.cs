using System.Collections.Concurrent;
using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Configuration;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Fact]
    public async Task CommandPreparationCapturesAndRevalidatesOriginalReviewWithoutAnyEffect()
    {
        using var f = new Fixture();
        var activities = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);
        await using var queue = f.QueueService();
        var service = new SessionWorkspaceService(f, new(f), f, f.Logger, queue);
        var preparation = await service.PrepareDiagnosticsPlanAsync(f.Request.SessionId, AllDiagnostics, () => true, f.Token);
        preparation.Work.Session.Authority.Should().Be(f.Session);
        preparation.Review.Profiles.Should().Equal(AllDiagnostics);
        await service.ValidateDiagnosticsPlanPreparationAsync(preparation, () => true, f.Token);
        activities.Should().Contain(activity => activity.Context == preparation.Cause && activity.Status == ActivityStatusCode.Ok);
        activities.Should().Contain(activity => activity.Links.Any(link => link.Context == preparation.Cause)
            && activity.Status == ActivityStatusCode.Ok);
        f.TaskWrites.Should().BeEmpty();
        f.QueueRows.Should().BeEmpty();
        f.QueueActions.Should().Be(0);
        // Validation does not consume the existing one-shot native confirmation.
        (await service.ConfirmDiagnosticsPlanAsync(preparation.Review, () => true, f.Token)).Items
            .Should().OnlyContain(item => item.State == SessionDiagnosticsPlanItemState.Accepted);
    }

    [Theory]
    [InlineData("queue")]
    [InlineData("control")]
    [InlineData("source")]
    [InlineData("generation")]
    [InlineData("privacy")]
    [InlineData("revision")]
    [InlineData("admission")]
    [InlineData("original")]
    [InlineData("cancel")]
    [InlineData("late-cancel")]
    [InlineData("retired")]
    [InlineData("limits")]
    [InlineData("disposed")]
    public async Task DeferredPreparationNeverReplacesAnyChangedOriginalSnapshot(string change)
    {
        using var f = new Fixture();
        await using var settings = new SessionQueueConfigurationTestFixture();
        await settings.Refresh();
        await using var queue = f.QueueService(configuration: settings.Service);
        var service = new SessionWorkspaceService(f, new(f), f, f.Logger, queue);
        var original = true;
        var preparation = await service.PrepareDiagnosticsPlanAsync(f.Request.SessionId, AllDiagnostics, () => original, f.Token);
        using var cancellation = new CancellationTokenSource();
        switch (change)
        {
            case "queue": f.AdvanceQueueRevision(); break;
            case "control": f.SetDiagnosticsControl(preparation.Review.Queue.DispatchControl! with { Paused = true }); break;
            case "source":
                f.ExactListReader = _ => preparation.Work.Session with
                { Metadata = new(f.Request.SessionId, new(1), new("changed source")) };
                break;
            case "generation": f.Session = f.Session with { Generation = new(2) }; break;
            case "privacy": f.CanInspect = false; break;
            case "revision": f.AdvanceRevision(); break;
            case "original": original = false; break;
            case "cancel": cancellation.Cancel(); break;
            case "retired": service.RetireDiagnosticsPlan(preparation.Review); break;
            case "limits": await settings.Set("1", SessionQueueOption.PendingPerSession); break;
            case "disposed": await queue.DisposeAsync(); break;
        }
        var validate = () => service.ValidateDiagnosticsPlanPreparationAsync(preparation, () =>
        {
            if (change is "late-cancel") { cancellation.Cancel(); }
            return change is not "admission";
        },
            cancellation.Token);
        await validate.Should().ThrowAsync<Exception>();
        f.TaskWrites.Should().BeEmpty();
        f.QueueRows.Should().BeEmpty();
        f.QueueActions.Should().Be(0);
    }

    [Theory]
    [InlineData("before")]
    [InlineData("during")]
    [InlineData("source")]
    [InlineData("privacy")]
    [InlineData("control")]
    [InlineData("cancel")]
    [InlineData("inside-admission")]
    public async Task ChangedPreparationAdmissionOrSourceRetiresRatherThanPublishingReview(string boundary)
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        var service = new SessionWorkspaceService(f, new(f), f, f.Logger, queue);
        using var cancellation = new CancellationTokenSource();
        if (boundary is "privacy") { f.CanInspect = false; }
        if (boundary is "control") { f.CanControl = false; }
        if (boundary is "during") { f.AfterQueueRead = f.AdvanceRevision; }
        if (boundary is "source")
        {
            f.ExactListReader = _ => new(f.Session, new(f.Request.SessionId, new(1), new("changed")));
        }
        var prepare = () => service.PrepareDiagnosticsPlanAsync(f.Request.SessionId, AllDiagnostics,
            () =>
            {
                if (boundary is "inside-admission") { f.AdvanceRevision(); }
                if (boundary is "cancel") { cancellation.Cancel(); }
                return boundary is not "before";
            }, cancellation.Token);
        await prepare.Should().ThrowAsync<Exception>();
        f.TaskWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task ConsumingOriginalReviewCannotBeReopenedWhileAuthoritativeEnqueueIsApplying()
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        var service = new SessionWorkspaceService(f, new(f), f, f.Logger, queue);
        var preparation = await service.PrepareDiagnosticsPlanAsync(f.Request.SessionId,
            [SessionQueueReadProfile.Version], () => true, f.Token);
        f.BeforeEnqueue = async _ =>
        {
            var validate = () => service.ValidateDiagnosticsPlanPreparationAsync(preparation, () => true, f.Token);
            await validate.Should().ThrowAsync<InvalidOperationException>();
        };
        (await service.ConfirmDiagnosticsPlanAsync(preparation.Review, () => true, f.Token)).Items
            .Should().ContainSingle().Which.State.Should().Be(SessionDiagnosticsPlanItemState.Accepted);
    }
}
