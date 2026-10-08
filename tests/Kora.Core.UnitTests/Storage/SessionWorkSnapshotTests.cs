using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SessionWorkSnapshotTests
{
    [Fact]
    public void Snapshot_identity_and_generation_cannot_follow_a_different_subject_or_supply_authority()
    {
        var session = new HostId<SessionIdentity>(Guid.NewGuid());
        var other = new HostId<SessionIdentity>(Guid.NewGuid());
        var snapshot = new SessionWorkSnapshot(new(new(session, new(1), true), null), 0,
            DateTimeOffset.UtcNow, new(session, new(1), 0, []), 0, 10, 1, [], 0, [], 0, [], 0);
        snapshot.RequireSubject(session);
        foreach (var malformed in new[]
        {
            snapshot with { Session = new(new(other, new(1), true), null) },
            snapshot with { Queue = new(other, new(1), 0, []) },
            snapshot with { Queue = new(session, new(2), 0, []) },
        })
        {
            var validate = () => malformed.RequireSubject(session);
            validate.Should().Throw<InvalidDataException>();
        }
        var unknown = () => snapshot.RequireSubject(default);
        unknown.Should().Throw<InvalidDataException>();
    }
}
