using AwesomeAssertions;

using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Hosting;

public sealed class SessionQueueCapacityTests
{
    [Fact]
    public void PendingCapacityIncludesExpiredProjectionsButNotForeignRunsRunningOrHistory()
    {
        var run = Guid.NewGuid();
        var now = DateTimeOffset.UnixEpoch;
        SessionQueueEntry Row(SessionQueueState state, Guid id) =>
            new(HostRequest.Create(RequestOrigin.LocalUi), new(1), new(1), 1, state, id, 1, now, now.AddMinutes(30));
        SessionQueueCapacity.PendingCount(
        [
            Row(SessionQueueState.Pending, run), Row(SessionQueueState.Expired, run),
            Row(SessionQueueState.Pending, Guid.NewGuid()), Row(SessionQueueState.Running, run),
            Row(SessionQueueState.Succeeded, run), Row(SessionQueueState.Cancelled, run),
        ], run).Should().Be(2);
        SessionQueueCapacity.PendingCount([], run).Should().Be(0);
    }
}
