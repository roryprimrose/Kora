using System.Text;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Authorization;
using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Commands;

public sealed class SessionQueueObservationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalTaskMetadataIsReadOnlyWireCompatibleAndNeverChangesStoredEntryBytes(bool includeMetadata)
    {
        var request = new HostRequest(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()), RequestOrigin.LocalUi);
        var now = DateTimeOffset.Parse("2026-10-12T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var entry = new SessionQueueEntry(request, new(1), new(1), 1, SessionQueueState.Pending,
            Guid.NewGuid(), 1, now, now.AddMinutes(30));
        var stored = JsonSerializer.Serialize(entry);
        var metadata = new HostTaskMetadata(request.TaskId, request.SessionId, new(1), new(2),
            HostTaskState.Interrupted, "unclassified", false);
        var row = new SessionQueueObservation(entry, SessionQueueEligibility.InterruptedNoReplay, null)
        {
            TaskMetadata = includeMetadata ? metadata : null,
        };
        var wire = JsonSerializer.Serialize(row);
        var decoded = JsonSerializer.Deserialize<SessionQueueObservation>(wire)!;
        decoded.TaskMetadata.Should().Be(includeMetadata ? metadata : null);
        JsonSerializer.Serialize(decoded.Entry).Should().Be(stored);
        if (includeMetadata)
        {
            decoded.TaskMetadata!.TaskId.Should().Be(request.TaskId);
            decoded.TaskMetadata.SessionId.Should().Be(request.SessionId);
            decoded.TaskMetadata.Generation.Value.Should().Be(1);
            decoded.TaskMetadata.Revision.Value.Should().Be(2);
            decoded.TaskMetadata.State.Should().Be(HostTaskState.Interrupted);
            decoded.TaskMetadata.Source.Should().Be("unclassified");
            decoded.TaskMetadata.CurrentSource.Should().BeFalse();
        }
        else { wire.Should().NotContain("TaskMetadata"); }
    }

    [Fact]
    public void CompleteMixedWorkProjectionIncludesActualQueueTaskMetadataUnderTheExactSixtyFourKibibyteBoundary()
    {
        var session = new HostId<SessionIdentity>(Guid.NewGuid());
        var now = DateTimeOffset.Parse("2026-10-12T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var request = new HostRequest(new(Guid.NewGuid()), session, new(Guid.NewGuid()), RequestOrigin.LocalUi);
        var entry = new SessionQueueEntry(request, new(1), new(1), 1, SessionQueueState.Pending,
            Guid.NewGuid(), 1, now, now.AddMinutes(30));
        var queue = new SessionQueueObservation(entry, SessionQueueEligibility.InterruptedNoReplay, null)
        {
            TaskMetadata = new(request.TaskId, session, new(1), new(2), HostTaskState.Interrupted, "unclassified", false),
        };
        var authority = new WorkSessionAuthorization(session, new(1), true);
        var snapshot = new SessionWorkSnapshot(new(authority, null), 1, now, new(session, new(1), 1, [entry]),
            1, 10, 1, [queue], 9,
            [new(new(request, new(3), HostTaskState.Succeeded), new(1), "unclassified", false, null)], 7, [], 0);
        var reply = new SessionCommandResult("observed", "") { Work = snapshot };
        var wire = Encoding.UTF8.GetString(SessionCommandResult.Serialize(reply));
        wire.Should().Contain("\"taskMetadata\":").And.Contain("\"state\":\"Interrupted\"")
            .And.Contain("\"omittedQueueRecords\":9").And.Contain("\"omittedTasks\":7")
            .And.NotContain("QuestionSpec").And.NotContain("Argument");
        var exact = reply with { Message = new string('x', SessionCommand.MaximumResultBytes - SessionCommandResult.GetSerializedSize(reply)) };
        SessionCommandResult.Serialize(exact).Length.Should().Be(SessionCommand.MaximumResultBytes);
        var overflow = () => SessionCommandResult.Serialize(exact with { Message = exact.Message + "x" });
        overflow.Should().Throw<InvalidDataException>().WithMessage("*64 KiB*");
        var worst = reply with
        {
            Work = snapshot with
            {
                QueueRecords = [.. Enumerable.Repeat(queue, SessionWorkSnapshot.MaximumRecords)],
                Tasks = [.. Enumerable.Repeat(snapshot.Tasks[0], SessionWorkSnapshot.MaximumRecords)],
                PendingQuestions = [.. Enumerable.Repeat(new SessionWorkQuestion(
                    new(request, new(Guid.NewGuid()), new(1)), new(1), QuestionStatus.Pending, QuestionKind.Text, null, now),
                    SessionWorkSnapshot.MaximumRecords)],
            },
        };
        SessionCommandResult.GetSerializedSize(worst).Should().BeGreaterThan(SessionCommand.MaximumResultBytes);
        var oversized = () => SessionCommandResult.Serialize(worst);
        oversized.Should().Throw<InvalidDataException>();
    }
}
