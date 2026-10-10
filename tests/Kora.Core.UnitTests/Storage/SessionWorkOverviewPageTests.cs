using System.Text;

using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SessionWorkOverviewPageTests
{
    [Fact]
    public void SerializationBoundsTheEntireProjectionWithoutTruncatingExactIdentities()
    {
        var id = new HostId<SessionIdentity>(Guid.NewGuid());
        var entry = new SessionWorkspaceEntry(new(id, new(2), false), new(id, new(3), new("Private 🐈")));
        var row = new SessionWorkOverviewRow(entry, SessionWorkOverviewAvailability.OutputBound, null);
        var cursor = new SessionWorkOverviewCursor(Guid.NewGuid(), 3, 2, id.Value, SessionListFilter.Done, true);
        var page = new SessionWorkOverviewPage([row], 1, 0, true, cursor);
        var bytes = SessionWorkOverviewPage.Serialize(page);
        bytes.Length.Should().Be(SessionWorkOverviewPage.GetSerializedSize(page));
        Encoding.UTF8.GetString(bytes).Should().Contain(id.Value.ToString("D"));
        page.Next.Should().Be(cursor);
        page.Records.Single().Work.Should().BeNull();
        var oversized = page with { Records = [.. Enumerable.Repeat(row, 500)] };
        var serialize = () => SessionWorkOverviewPage.Serialize(oversized);
        serialize.Should().Throw<InvalidDataException>();
        SessionWorkOverviewPage.Scope.Should().Contain("not globally atomic").And.Contain("No full-scope totals");
    }
}
