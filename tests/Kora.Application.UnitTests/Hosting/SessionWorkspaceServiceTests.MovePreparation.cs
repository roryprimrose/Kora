using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    private static SessionQueueMoveReviewCommand Preparation(Fixture f, bool up = true) =>
        new(f.Request.SessionId.Value, f.QueueRows[up ? 1 : 0].Request.TaskId.Value,
            f.QueueRows[up ? 0 : 1].Request.TaskId.Value, up);

    [Theory]
    [InlineData(true, RequestOrigin.LocalUi)]
    [InlineData(false, RequestOrigin.ActivatedVoice)]
    public async Task PreparationUsesSameAuthoritativeReviewWithoutMutationAndHandoffIsNativeOneShot(bool up, RequestOrigin origin)
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        var service = new SessionWorkspaceService(f, new(f), f, f.Logger, queue);
        await AddMovePair(f, queue);
        using var root = Kora.Core.Diagnostics.HostActivity.BeginRoot(f.Request,
            Kora.Core.Diagnostics.HostActivityLayer.Desktop, Kora.Core.Diagnostics.HostOperation.Request);
        var before = f.QueueRows.ToArray();
        var writes = f.TaskWrites.Count;
        var prepared = await service.PrepareNativeMoveReviewAsync(Preparation(f, up), origin, () => true, f.Token);
        prepared.Work.Queue.MatchesRecordedState(prepared.Review.Queue).Should().BeTrue();
        prepared.Review.Selected.Should().Be(before[up ? 1 : 0]);
        prepared.Review.Neighbor.Should().Be(before[up ? 0 : 1]);
        prepared.Review.Up.Should().Be(up);
        f.QueueRows.Should().Equal(before);
        f.TaskWrites.Should().HaveCount(writes);
        f.QueueActions.Should().Be(0);
        var result = await service.ExecuteCommandAsync(ConfirmMove(prepared.Review), RequestOrigin.LocalUi, () => true, f.Token);
        result.Queue!.Entries.Should().Equal(before.Reverse());
        var replay = () => service.ExecuteCommandAsync(ConfirmMove(prepared.Review), RequestOrigin.LocalUi, () => true, f.Token);
        await replay.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("error")]
    [InlineData("session")]
    [InlineData("task")]
    [InlineData("neighbor")]
    [InlineData("same")]
    [InlineData("origin")]
    public async Task InvalidStrongInputsNeverReadOrWriteQueue(string mode)
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        await AddMovePair(f, queue);
        var command = Preparation(f);
        command = mode switch
        {
            "error" => command with { Error = "invalid" },
            "session" => command with { SessionId = Guid.Empty },
            "task" => command with { TaskId = Guid.Empty },
            "neighbor" => command with { NeighborId = Guid.Empty },
            "same" => command with { NeighborId = command.TaskId },
            _ => command,
        };
        var act = () => queue.PrepareNativeMoveReviewAsync(command,
            mode is "origin" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi, () => true, f.Token);
        await act.Should().ThrowAsync<InvalidOperationException>();
        f.QueueActions.Should().Be(0);
    }

    [Theory]
    [InlineData("done")]
    [InlineData("control")]
    [InlineData("selected")]
    [InlineData("neighbor")]
    [InlineData("held")]
    [InlineData("expired")]
    [InlineData("run")]
    [InlineData("private")]
    [InlineData("generation")]
    [InlineData("nonadjacent")]
    [InlineData("dependency")]
    [InlineData("foreign")]
    [InlineData("unavailable")]
    public async Task WholePreparationRefusesIneligibleOrUncertainExactSource(string mode)
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        await AddMovePair(f, queue);
        var exact = Preparation(f);
        if (mode is "selected") { exact = exact with { TaskId = Guid.NewGuid() }; }
        if (mode is "neighbor") { exact = exact with { NeighborId = Guid.NewGuid() }; }
        if (mode is "done") { f.Session = f.Session with { IsActive = false }; }
        if (mode is "unavailable") { f.QueueFailure = "read"; }
        if (mode is "foreign") { f.ForeignWork = true; }
        if (mode is "dependency") { f.QueueRows[1] = f.QueueRows[1] with { Dependency = f.QueueRows[0].Request.TaskId }; }
        if (mode is "expired") { f.QueueTime.UtcNow += TimeSpan.FromHours(1); }
        f.QueueReadProjection = value => mode switch
        {
            "control" => value with { DispatchControl = null },
            "held" => value with { DispatchControl = value.DispatchControl! with { Held = true } },
            "run" => value with { Entries = [.. value.Entries.Select(e => e with { RunId = Guid.NewGuid() })] },
            "private" => value with { Entries = [.. value.Entries.Select(e => e with { AdmissionRevision = 99 })] },
            "generation" => value with { Entries = [.. value.Entries.Select(e => e with { Generation = new(99) })] },
            "nonadjacent" => value with { Entries = [value.Entries[0], value.Entries[0] with
                { Request = new(new(Guid.NewGuid()), value.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi) }, value.Entries[1]] },
            _ => value,
        };
        var writes = f.TaskWrites.Count;
        var act = () => queue.PrepareNativeMoveReviewAsync(exact, RequestOrigin.LocalUi, () => true, f.Token);
        await act.Should().ThrowAsync<Exception>();
        f.TaskWrites.Should().HaveCount(writes);
        f.QueueActions.Should().Be(0);
    }

    [Theory]
    [InlineData("revision", 2)]
    [InlineData("revision", 3)]
    [InlineData("order", 2)]
    [InlineData("order", 3)]
    [InlineData("lineage", 3)]
    [InlineData("neighbor", 2)]
    [InlineData("metadata", 2)]
    [InlineData("admission", 2)]
    [InlineData("cancel", 2)]
    [InlineData("config", 2)]
    [InlineData("deadline", 3)]
    public async Task DeferredSourceChangesNeverPublishADifferentPairOrRetry(string mode, int atRead)
    {
        using var f = new Fixture();
        await using var settings = new SessionQueueConfigurationTestFixture();
        settings.Service.Observe();
        await using var queue = f.QueueService(configuration: settings.Service);
        await AddMovePair(f, queue);
        var exact = Preparation(f);
        using var cancellation = new CancellationTokenSource();
        var admission = true;
        var reads = 0;
        f.QueueReadProjection = value =>
        {
            if (++reads != atRead) { return value; }
            switch (mode)
            {
                case "revision": return value with { Revision = value.Revision + 1 };
                case "order": return value with { Entries = [.. value.Entries.Reverse()] };
                case "lineage": return value with { Orders = [new(1, Guid.NewGuid(), value.SessionId, value.Generation,
                    value.DispatchControl!.RunId, value.Revision, [.. value.Entries.Reverse().Select(e => e.Request.TaskId)])] };
                case "neighbor": return value with { Entries = [value.Entries[0] with { Revision = new(2) }, value.Entries[1]] };
                case "metadata": f.ExactListReader = _ => new(f.Session, new(f.Request.SessionId, new(1), new("Changed"))); break;
                case "admission": admission = false; break;
                case "cancel": cancellation.Cancel(); break;
                case "config": settings.Service.HoldUnavailable(); break;
                case "deadline": f.QueueTime.UtcNow += TimeSpan.FromHours(1); break;
            }
            return value;
        };
        var writes = f.TaskWrites.Count;
        var act = () => queue.PrepareNativeMoveReviewAsync(exact, RequestOrigin.LocalUi, () => admission, cancellation.Token);
        await act.Should().ThrowAsync<Exception>();
        f.TaskWrites.Should().HaveCount(writes);
        f.QueueActions.Should().Be(0);
    }

    [Fact]
    public async Task NativeHandoffRetainsOriginalSourceAndConfigAdmissionUntilSeparateConfirmation()
    {
        using var f = new Fixture();
        await using var settings = new SessionQueueConfigurationTestFixture();
        settings.Service.Observe();
        await using var queue = f.QueueService(configuration: settings.Service);
        await AddMovePair(f, queue);
        var original = true;
        var prepared = await queue.PrepareNativeMoveReviewAsync(Preparation(f), RequestOrigin.ActivatedVoice, () => original, f.Token);
        original = false;
        var confirm = () => queue.ExecuteCommandAsync(ConfirmMove(prepared.Review), RequestOrigin.LocalUi, () => true, f.Token);
        await confirm.Should().ThrowAsync<InvalidOperationException>();
        f.QueueActions.Should().Be(0);
    }

    [Fact]
    public async Task MissingAuthoritativeServiceHasNoFallback()
    {
        using var f = new Fixture();
        var act = () => f.Service.PrepareNativeMoveReviewAsync(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), true),
            RequestOrigin.LocalUi, () => true, f.Token);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("inspect")]
    [InlineData("control")]
    [InlineData("private-revision")]
    [InlineData("disposed")]
    [InlineData("cancel")]
    [InlineData("config")]
    public async Task OriginalPrivateAndConfigurationAdmissionCannotBeRetiredAndReused(string mode)
    {
        using var f = new Fixture();
        await using var settings = new SessionQueueConfigurationTestFixture();
        settings.Service.Observe();
        await using var queue = f.QueueService(configuration: settings.Service);
        await AddMovePair(f, queue);
        using var cancellation = new CancellationTokenSource();
        var prepared = await queue.PrepareNativeMoveReviewAsync(Preparation(f), RequestOrigin.LocalUi, () => true, cancellation.Token);
        prepared.IsCurrent().Should().BeTrue();
        switch (mode)
        {
            case "inspect": f.CanInspect = false; break;
            case "control": f.CanControl = false; break;
            case "private-revision": f.AdvanceRevision(); break;
            case "disposed": await queue.DisposeAsync(); break;
            case "cancel": cancellation.Cancel(); break;
            case "config": settings.PreferencesStore.Value = new(pendingPerSession: 1); break;
        }
        prepared.IsCurrent().Should().BeFalse();
        f.QueueRows.Should().Equal(prepared.Work.Queue.Entries);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BothOriginalDeadlinesRetirePreparedReviewWithoutRenewal(bool selectedFirst)
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        await AddMovePair(f, queue);
        var later = selectedFirst ? 0 : 1;
        f.QueueRows[later] = f.QueueRows[later] with
        {
            EnqueuedAt = f.QueueRows[later].EnqueuedAt.AddMinutes(1),
            ExpiresAt = f.QueueRows[later].ExpiresAt.AddMinutes(1),
        };
        var prepared = await queue.PrepareNativeMoveReviewAsync(Preparation(f), RequestOrigin.LocalUi, () => true, f.Token);
        prepared.IsCurrent().Should().BeTrue();
        f.QueueTime.UtcNow = f.QueueRows[selectedFirst ? 1 : 0].ExpiresAt;
        prepared.IsCurrent().Should().BeFalse();
    }

    [Fact]
    public async Task OriginalAdmissionIsCheckedAgainAtPhysicalMoveFenceNotJustWhenReviewIsConsumed()
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        await AddMovePair(f, queue);
        var admitted = true;
        var prepared = await queue.PrepareNativeMoveReviewAsync(Preparation(f), RequestOrigin.LocalUi, () => admitted, f.Token);
        f.BeforeMove = () => admitted = false;
        var confirm = () => queue.ExecuteCommandAsync(ConfirmMove(prepared.Review), RequestOrigin.LocalUi, () => true, f.Token);
        await confirm.Should().ThrowAsync<InvalidOperationException>();
        f.QueueRows.Should().Equal(prepared.Work.Queue.Entries);
        f.TaskWrites.Last().State.Should().Be(HostTaskState.Denied);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreparationActivitiesEndTruthfullyAndGeneratedFailureLogsContainNoPrivateIds(bool cancel)
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        await AddMovePair(f, queue);
        var observed = new List<System.Diagnostics.Activity>();
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) => System.Diagnostics.ActivitySamplingResult.AllData,
            ActivityStopped = observed.Add,
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);
        using var cancellation = new CancellationTokenSource();
        if (cancel) { cancellation.Cancel(); }
        else { f.QueueFailure = "read"; }
        var act = () => queue.PrepareNativeMoveReviewAsync(Preparation(f), RequestOrigin.LocalUi, () => true, cancellation.Token);
        await act.Should().ThrowAsync<Exception>();
        observed.Should().ContainSingle().Which.GetTagItem("kora.outcome").Should().Be(cancel ? "Cancelled" : "Failed");
        observed[0].Baggage.Should().BeEmpty();
        if (!cancel)
        {
            f.QueueEvents.Should().Contain(e => e.Id == 184);
            f.QueueMessages.Should().OnlyContain(message => !message.Contains(f.Request.SessionId.Value.ToString("D"), StringComparison.Ordinal));
        }
    }
}
