using AwesomeAssertions;
using System.Globalization;

using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Commands;

public sealed class SessionQueueCancellationCommandTests
{
    private const string Session = "11111111-1111-1111-1111-111111111111";
    private const string TaskId = "22222222-2222-2222-2222-222222222222";
    private const string Run = "33333333-3333-3333-3333-333333333333";
    private const string Review = "44444444-4444-4444-4444-444444444444";
    private const string Prefix = Session + " 1 0 " + Run + " 0 ";

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 10)]
    [InlineData(true, 10)]
    public void ExactTypedAndCurrentNameActivatedCommandsBindTheWholeBoundedList(bool confirm, int count)
    {
        var members = string.Join(",", Enumerable.Range(1, count).Select(index => index.ToString("D8", CultureInfo.InvariantCulture) + "-0000-0000-0000-000000000000:1"));
        var input = "queue cancel-selected-" + (confirm ? "confirm " : "review ") + Prefix + members + (confirm ? " " + Review + " confirm" : "");
        var command = SessionCommand.Parse(input, "Kora")!;
        command.Operation.Should().Be(confirm ? SessionCommandOperation.QueueCancelSelectedConfirm : SessionCommandOperation.QueueCancelSelectedReview);
        command.SessionId.Should().Be(Guid.Parse(Session));
        command.Generation.Should().Be(1);
        command.QueueRevision.Should().Be(0);
        command.QueueRunId.Should().Be(Guid.Parse(Run));
        command.QueueControlRevision.Should().Be(0);
        command.QueueSelection.Should().HaveCount(count);
        command.QueueSelection.Should().OnlyContain(member => member.Revision.Value == 1);
        command.QueueCancelReviewId.Should().Be(confirm ? Guid.Parse(Review) : null);
        SessionCommand.Parse("Kora " + input, "Kora")!.Should().BeEquivalentTo(command);
        SessionCommand.Parse("queue cancel-selected-discard " + Session + " " + Review, "Kora")!.QueueCancelReviewId.Should().Be(Guid.Parse(Review));
    }

    [Theory]
    [InlineData("review", "")]
    [InlineData("review", TaskId + ":1,")]
    [InlineData("review", TaskId + ":1," + TaskId + ":2")]
    [InlineData("review", TaskId + ":0")]
    [InlineData("review", TaskId + ":01")]
    [InlineData("review", TaskId + ":+1")]
    [InlineData("review", TaskId + ":1:2")]
    [InlineData("review", "{22222222-2222-2222-2222-222222222222}:1")]
    [InlineData("review", "aaaaaaaa-AAAA-aaaa-aaaa-aaaaaaaaaaaa:1")]
    [InlineData("review", "00000000-0000-0000-0000-000000000000:1")]
    [InlineData("confirm", TaskId + ":1")]
    [InlineData("confirm", TaskId + ":1 invalid confirm")]
    [InlineData("confirm", TaskId + ":1 " + Review + " yes")]
    [InlineData("review", TaskId + ":1 unexpected")]
    public void StrictCanonicalMembersAndExactConfirmationRejectMalformedOrPartialCommands(string verb, string members)
    {
        SessionCommand.Parse("queue cancel-selected-" + verb + " " + Prefix + members, "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);
    }

    [Fact]
    public void SharedInputBoundsAndLegacyCancelClearRemainUnchanged()
    {
        var members = string.Join(",", Enumerable.Range(1, 11).Select(index => index.ToString("D8", CultureInfo.InvariantCulture) + "-0000-0000-0000-000000000000:1"));
        SessionCommand.Parse("queue cancel-selected-review " + Prefix + members, "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);
        SessionCommand.Parse("queue cancel-selected-review " + Prefix + TaskId + ":1" + new string(' ', 1024), "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);
        SessionCommand.Parse("queue cancel-selected-review " + Session + " 1 0 invalid 0 " + TaskId + ":1", "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);
        SessionCommand.Parse("queue cancel-selected-review " + Session + " 1 0 " + Run + " -1 " + TaskId + ":1", "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);
        foreach (var malformedPrefix in new[]
        {
            "AAAAAAAA-aaaa-aaaa-aaaa-aaaaaaaaaaaa 1 0 " + Run + " 0 ",
            Session + " 01 0 " + Run + " 0 ", Session + " 1 00 " + Run + " 0 ",
        })
        {
            SessionCommand.Parse("queue cancel-selected-review " + malformedPrefix + TaskId + ":1", "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);
        }
        SessionCommand.Parse("queue cancel " + Session + " 1 0 " + TaskId + " 1", "Kora")!.Operation.Should().Be(SessionCommandOperation.QueueCancel);
        SessionCommand.Parse("queue clear " + Session + " 1 0 confirm", "Kora")!.Operation.Should().Be(SessionCommandOperation.QueueClear);
        var result = new SessionCommandResult("reviewed", new string('x', SessionCommand.MaximumResultBytes))
        {
            QueueCancelReview = new(Guid.NewGuid(), new(new(Guid.Parse(Session)), new(1), 0, []), [], 1, DateTimeOffset.UtcNow),
            QueueCancelReceipt = new(Guid.NewGuid(), 0, Guid.Parse(Run), 0, 1, [], DateTimeOffset.UtcNow),
        };
        result.Invoking(value => SessionCommandResult.Serialize(value)).Should().Throw<InvalidDataException>();
    }
}
