using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Fact]
    public async Task Work_snapshots_and_exact_queue_list_share_the_same_bounded_host_read_without_activity_writes()
    {
        using var fixture = new Fixture();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var snapshot = await fixture.Service.ReadWorkAsync(fixture.Request.SessionId, fixture.Token);
        snapshot.PendingCapacity.Should().Be(10);
        fixture.TaskWrites.Should().BeEmpty();
        fixture.ControlCalls.Should().Be(0);
        await using var queue = fixture.QueueService(slots: 2);
        var service = new SessionWorkspaceService(fixture, new(fixture), fixture, fixture.Logger, queue);
        var native = await service.ReadWorkAsync(fixture.Request.SessionId, fixture.Token);
        var command = await service.ExecuteCommandAsync(fixture.QueueCommand(SessionCommandOperation.QueueList),
            RequestOrigin.ActivatedVoice, () => true, fixture.Token);
        command.Work.Should().BeEquivalentTo(native);
        command.Queue.Should().BeSameAs(command.Work!.Queue);
        fixture.TaskWrites.Should().BeEmpty();
        fixture.QueueActions.Should().Be(0);
        native.ExecutionSlots.Should().Be(2);
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("revision")]
    [InlineData("cancel")]
    public async Task Work_snapshot_invalidations_fail_closed_without_retargeting_or_terminal_writes(string boundary)
    {
        using var fixture = new Fixture();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        fixture.AfterQueueRead = () =>
        {
            if (boundary is "privacy") { fixture.CanInspect = false; }
            else if (boundary is "revision") { fixture.AdvanceRevision(); }
            else { throw new OperationCanceledException("Work read cancelled."); }
        };
        var read = () => fixture.Service.ReadWorkAsync(fixture.Request.SessionId, fixture.Token);
        await read.Should().ThrowAsync<Exception>();
        fixture.TaskWrites.Should().BeEmpty();
        fixture.ControlCalls.Should().Be(0);
    }

    [Fact]
    public async Task Missing_authoritative_work_store_is_unavailable_not_an_empty_ledger()
    {
        using var fixture = new Fixture();
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var missing = new SessionWorkspaceService(null!, new(fixture), fixture, fixture.Logger);
        var read = () => missing.ReadWorkAsync(fixture.Request.SessionId, fixture.Token);
        await read.Should().ThrowAsync<InvalidOperationException>().WithMessage("*snapshot service is unavailable*");
        await using var queue = new SessionQueueService(null!, fixture, new(fixture), fixture, fixture, new(),
            NullLogger<SessionQueueService>.Instance);
        var list = () => queue.ExecuteCommandAsync(fixture.QueueCommand(SessionCommandOperation.QueueList),
            RequestOrigin.LocalUi, () => true, fixture.Token);
        await list.Should().ThrowAsync<InvalidOperationException>().WithMessage("*snapshot service is unavailable*");
    }

    private sealed partial class Fixture : ISessionWorkStore
    {
        internal bool ForeignWork { get; set; }
        public async ValueTask<SessionWorkSnapshot> ReadWorkAsync(HostId<SessionIdentity> session, long admissionRevision,
            SessionQueueLimits limits, CancellationToken token)
        {
            var observed = await ReadQueueAsync(session, token);
            return new(new(ForeignWork ? Session with { SessionId = new(Guid.NewGuid()) } : Session, null), 1, QueueTime.GetUtcNow(), observed, observed.Entries.Count,
                limits.PendingPerSession, limits.ExecutionSlots,
                [.. observed.Entries.Select(entry => new SessionQueueObservation(entry, SessionQueueEligibility.Ready, null))],
                0, [], 0, [], 0);
        }
    }

    [Fact]
    public async Task Cross_session_snapshot_never_retargets_native_or_exact_command_presentation()
    {
        using var fixture = new Fixture { ForeignWork = true };
        using var root = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var read = () => fixture.Service.ReadWorkAsync(fixture.Request.SessionId, fixture.Token);
        await read.Should().ThrowAsync<InvalidDataException>();
        await using var queue = fixture.QueueService();
        var command = () => queue.ExecuteCommandAsync(fixture.QueueCommand(SessionCommandOperation.QueueList),
            RequestOrigin.LocalUi, () => true, fixture.Token);
        await command.Should().ThrowAsync<InvalidDataException>();
        fixture.TaskWrites.Should().BeEmpty();
    }
}
