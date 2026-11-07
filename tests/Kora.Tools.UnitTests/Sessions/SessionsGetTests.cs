using AwesomeAssertions;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Kora.Tools.Sessions;

namespace Kora.Tools.UnitTests.Sessions;

public sealed class SessionsGetTests
{
    [Fact]
    public void Only_the_exact_reviewed_subject_is_returned_without_other_source_access()
    {
        const string id = "10000000-0000-0000-0000-000000000001";
        var snapshot = new SessionMetadataSnapshot(id, null, false, SessionWorkOverviewAvailability.ReadUnavailable,
            null, null, null, null, null, null, null, null, null);
        var action = new SessionsGet();
        var result = action.Execute(snapshot, $$"""{"id":"{{id}}"}""");
        result.SessionMetadata.Should().BeSameAs(snapshot);
        result.Outcome.Should().Be(CapabilityOutcome.Succeeded);
        action.Execute(snapshot, "{}").Outcome.Should().Be(CapabilityOutcome.Denied);
        action.Execute(snapshot, """{"id":"10000000-0000-0000-0000-000000000002"}""").Outcome.Should().Be(CapabilityOutcome.Denied);
        action.Invoking(value => value.Execute(snapshot with { SessionId = "bad" }, "{}")).Should().Throw<InvalidDataException>();
        action.Invoking(value => value.Execute(null!, "{}")).Should().Throw<ArgumentNullException>();
    }
}
