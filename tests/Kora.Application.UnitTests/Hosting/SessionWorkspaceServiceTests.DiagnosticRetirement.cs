using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Fact]
    public async Task ExistingQueueInspectionWithoutAnInternalServiceRemainsExplicitlyUnavailable()
    {
        using var f = new Fixture();
        var unavailable = () => f.Service.ExecuteCommandAsync(f.QueueCommand(SessionCommandOperation.QueueList),
            RequestOrigin.LocalUi, () => true, f.Token);
        await unavailable.Should().ThrowAsync<InvalidOperationException>().WithMessage("*unavailable*");
        f.TaskWrites.Should().BeEmpty();
    }
    [Theory]
    [InlineData(SessionCommandOperation.QueueEnqueue)]
    [InlineData(SessionCommandOperation.QueueDispatch)]
    [InlineData(SessionCommandOperation.QueuePause)]
    [InlineData(SessionCommandOperation.QueueResume)]
    [InlineData(SessionCommandOperation.QueueMoveUp)]
    [InlineData(SessionCommandOperation.QueueMoveDown)]
    [InlineData(SessionCommandOperation.QueueMoveConfirm)]
    [InlineData(SessionCommandOperation.QueueMoveCancel)]
    [InlineData(SessionCommandOperation.QueueRequeueExpiredReview)]
    [InlineData(SessionCommandOperation.QueueRequeueExpiredConfirm)]
    [InlineData(SessionCommandOperation.QueueRequeueExpiredDiscard)]
    public async Task RetiredDiagnosticProductOperationsCannotMutateOrDispatchExistingRecords(SessionCommandOperation operation)
    {
        using var f = new Fixture();
        await using var queue = f.QueueService();
        await queue.ExecuteCommandAsync(f.QueueCommand(SessionCommandOperation.QueueEnqueue),
            RequestOrigin.LocalUi, () => true, f.Token);
        var before = f.QueueRows.ToArray();
        var writes = f.TaskWrites.Count;
        var workspace = new SessionWorkspaceService(f, new(f), f, f.Logger, queue);

        var attempt = () => workspace.ExecuteCommandAsync(f.QueueCommand(operation),
            RequestOrigin.LocalUi, () => true, f.Token);

        await attempt.Should().ThrowAsync<InvalidOperationException>().WithMessage("*retired*");
        f.QueueRows.Should().Equal(before);
        f.TaskWrites.Should().HaveCount(writes);
        f.QueueActions.Should().Be(0);
        var observed = await workspace.ExecuteCommandAsync(f.QueueCommand(SessionCommandOperation.QueueList),
            RequestOrigin.LocalUi, () => true, f.Token);
        observed.Outcome.Should().Be("observed");
        observed.Queue!.Entries.Should().Equal(before);
    }
}
