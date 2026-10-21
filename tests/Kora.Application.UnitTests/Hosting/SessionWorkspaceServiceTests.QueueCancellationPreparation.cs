using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Fact]
    public async Task MissingObservationAndAdmissionLostAfterReadCannotCreatePreparation()
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        var workspace = new SessionWorkspaceService(f, new(f), f, f.Logger, queue);
        await AddMovePair(f, queue);
        var command = new SessionQueueCancellationReviewCommand(f.Request.SessionId.Value, [f.QueueRows[0].Request.TaskId.Value]);
        var missing = () => SessionWorkspaceService.FreezeQueueCancellationPreparation(command, null, f.ControlRevision);
        missing.Should().Throw<InvalidDataException>();
        var writes = f.TaskWrites.Count;
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        // Deny when the completed observation hands control back to the original caller.
        var late = () => workspace.PrepareQueueCancellationAsync(command, RequestOrigin.LocalUi,
            () => HostActivity.Current?.Request != f.Request, f.Token);
        await late.Should().ThrowAsync<OperationCanceledException>();
        f.TaskWrites.Should().HaveCount(writes);
        f.QueueActions.Should().Be(0);
    }

    [Fact]
    public async Task PreparationFreezesEveryExplicitSubsetInDispatchOrderWithoutAnyMutationOrConfirmation()
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        var workspace = new SessionWorkspaceService(f, new(f), f, f.Logger, queue);
        for (var index = 0; index < SessionQueueLimits.MaximumPendingPerSession; index++)
        {
            await queue.ExecuteCommandAsync(f.QueueCommand(SessionCommandOperation.QueueEnqueue) with { QueueRevision = f.QueueRevision },
                RequestOrigin.LocalUi, () => true, f.Token);
        }
        var rows = f.QueueRows.ToArray();
        var writes = f.TaskWrites.Count;
        for (var mask = 1; mask < 1 << rows.Length; mask++)
        {
            var subset = rows.Where((_, index) => (mask & 1 << index) != 0).ToArray();
            var command = new SessionQueueCancellationReviewCommand(f.Request.SessionId.Value,
                [.. subset.Reverse().Select(entry => entry.Request.TaskId.Value)]);
            var prepared = await workspace.PrepareQueueCancellationAsync(command,
                mask % 2 == 0 ? RequestOrigin.LocalUi : RequestOrigin.ActivatedVoice, () => true, f.Token);
            prepared.Selection.Should().Equal(subset.Select(entry => new SessionQueueSelection(entry.Request.TaskId, entry.Revision)));
            prepared.ControlRevision.Should().Be(f.ControlRevision);
            prepared.Work.Queue.Entries.Should().Equal(rows);
            prepared.Work.Queue.DispatchControl!.RunId.Should().Be(f.QueueRun);
        }
        f.TaskWrites.Should().HaveCount(writes);
        f.QueueRows.Should().Equal(rows);
        f.QueueActions.Should().Be(0);
    }

    [Theory]
    [InlineData("error")]
    [InlineData("help")]
    [InlineData("session")]
    [InlineData("default")]
    [InlineData("empty")]
    [InlineData("too-many")]
    [InlineData("empty-id")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("origin")]
    [InlineData("admission")]
    [InlineData("control")]
    [InlineData("cancel")]
    public async Task PreparationRejectsInvalidOrUnadmittedWholeRequests(string failure)
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        var workspace = new SessionWorkspaceService(f, new(f), f, f.Logger, queue);
        await AddMovePair(f, queue);
        var rows = f.QueueRows.ToArray();
        var writes = f.TaskWrites.Count;
        var exact = new SessionQueueCancellationReviewCommand(f.Request.SessionId.Value, [rows[0].Request.TaskId.Value]);
        var command = failure switch
        {
            "error" => exact with { Error = "invalid" },
            "help" => exact with { IsHelp = true },
            "session" => exact with { SessionId = Guid.Empty },
            "default" => exact with { TaskIds = default },
            "empty" => exact with { TaskIds = [] },
            "too-many" => exact with { TaskIds = [.. Enumerable.Range(0, 11).Select(_ => Guid.NewGuid())] },
            "empty-id" => exact with { TaskIds = [Guid.Empty] },
            "duplicate" => exact with { TaskIds = [exact.TaskIds[0], exact.TaskIds[0]] },
            "unknown" => exact with { TaskIds = [exact.TaskIds[0], Guid.NewGuid()] },
            _ => exact,
        };
        if (failure is "control") { f.CanControl = false; }
        using var cancel = new CancellationTokenSource();
        if (failure is "cancel") { cancel.Cancel(); }
        var act = () => workspace.PrepareQueueCancellationAsync(command,
            failure is "origin" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi,
            () => failure is not "admission", cancel.Token);
        await act.Should().ThrowAsync<Exception>();
        f.TaskWrites.Should().HaveCount(writes);
        f.QueueRows.Should().Equal(rows);
        f.QueueActions.Should().Be(0);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("missing-control")]
    [InlineData("nonpending")]
    [InlineData("run")]
    [InlineData("task-session")]
    [InlineData("task-generation")]
    [InlineData("replaced")]
    [InlineData("revision")]
    [InlineData("late-control")]
    [InlineData("late-admission")]
    [InlineData("late-cancel")]
    public async Task PreparationRefusesWholeSubsetWhenOriginalObservedSourceIsNotEligible(string failure)
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        var workspace = new SessionWorkspaceService(f, new(f), f, f.Logger, queue);
        await AddMovePair(f, queue);
        using var activity = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var work = await workspace.ReadWorkAsync(f.Request.SessionId, f.Token);
        var rows = f.QueueRows.ToArray();
        var command = new SessionQueueCancellationReviewCommand(f.Request.SessionId.Value, [.. rows.Select(entry => entry.Request.TaskId.Value)]);
        using var cancel = new CancellationTokenSource();
        var admitted = true;
        f.OverviewReader = (_, _) =>
        {
            if (failure is "late-control") { f.AdvanceRevision(); }
            if (failure is "late-admission") { admitted = false; }
            if (failure is "late-cancel") { cancel.Cancel(); }
            var changed = rows[1];
            changed = failure switch
            {
                "nonpending" => changed with { State = SessionQueueState.Running },
                "run" => changed with { RunId = Guid.NewGuid() },
                "task-session" => changed with { Request = new(changed.Request.RequestId, new(Guid.NewGuid()), changed.Request.TaskId, changed.Request.Origin) },
                "task-generation" => changed with { Generation = new(2) },
                "replaced" => changed with { Request = new(changed.Request.RequestId, changed.Request.SessionId, new(Guid.NewGuid()), changed.Request.Origin) },
                "revision" => changed with { Revision = new(0) },
                _ => changed,
            };
            return Task.FromResult(work with
            {
                Session = failure is "inactive" ? work.Session with { Authority = work.Session.Authority with { IsActive = false } } : work.Session,
                Queue = work.Queue with { Entries = [rows[0], changed], DispatchControl = failure is "missing-control" ? null : work.Queue.DispatchControl },
            });
        };
        var writes = f.TaskWrites.Count;
        var act = () => workspace.PrepareQueueCancellationAsync(command, RequestOrigin.LocalUi, () => admitted, cancel.Token);
        await act.Should().ThrowAsync<Exception>();
        f.QueueRows.Should().Equal(rows);
        f.TaskWrites.Should().HaveCount(writes);
        f.QueueActions.Should().Be(0);
    }
}
