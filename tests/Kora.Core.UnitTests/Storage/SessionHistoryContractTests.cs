using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SessionHistoryContractTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    public void Pagination_accepts_only_bounded_exact_session_snapshot(int limit)
    {
        var session = new HostId<SessionIdentity>(Guid.NewGuid());
        new SessionHistoryCursor(session, new(1), 25, 10).Validate(session);
        new SessionHistoryCursor(session, new(1), 0, 0).Validate(session);
        SessionHistoryPage.ValidateLimit(limit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void Page_limits_fail_closed(int limit)
    {
        var act = () => SessionHistoryPage.ValidateLimit(limit);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("session")]
    [InlineData("generation")]
    [InlineData("snapshot")]
    [InlineData("after")]
    [InlineData("ahead")]
    public void Unknown_or_cross_session_cursor_cannot_be_retargeted(string invalid)
    {
        var session = new HostId<SessionIdentity>(Guid.NewGuid());
        var cursor = new SessionHistoryCursor(invalid is "session" ? new(Guid.NewGuid()) : session,
            invalid is "generation" ? default : new(1), invalid is "snapshot" ? -1 : 1,
            invalid is "after" ? -1 : invalid is "ahead" ? 2 : 0);
        var act = () => cursor.Validate(session);
        act.Should().Throw<ArgumentException>();
    }
}
