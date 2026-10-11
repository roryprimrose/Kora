using AwesomeAssertions;

using Kora.Core.Commands;

namespace Kora.Core.UnitTests.Commands;

public sealed class SessionQueueMoveCommandTests
{
    private const string Session = "11111111-1111-1111-1111-111111111111";
    private const string Run = "22222222-2222-2222-2222-222222222222";
    private const string Task = "33333333-3333-3333-3333-333333333333";
    private const string Neighbor = "44444444-4444-4444-4444-444444444444";
    private const string Review = "55555555-5555-5555-5555-555555555555";
    private const string Pair = Session + " 1 0 " + Run + " 0 " + Task + " 1 " + Neighbor + " 2";

    [Theory]
    [InlineData("move-up", SessionCommandOperation.QueueMoveUp)]
    [InlineData("move-down", SessionCommandOperation.QueueMoveDown)]
    [InlineData("move-confirm", SessionCommandOperation.QueueMoveConfirm)]
    public void ExactPairGrammarBindsEveryIdentityRevisionAndSeparateConfirmation(string verb, SessionCommandOperation operation)
    {
        var command = SessionCommand.Parse("queue " + verb + " " + Pair + (verb is "move-confirm" ? " " + Review + " confirm" : ""), "Kora")!;
        command.Operation.Should().Be(operation);
        command.SessionId.Should().Be(Guid.Parse(Session));
        command.Generation.Should().Be(1);
        command.QueueRevision.Should().Be(0);
        command.QueueRunId.Should().Be(Guid.Parse(Run));
        command.QueueControlRevision.Should().Be(0);
        command.TaskId.Should().Be(Guid.Parse(Task));
        command.TaskRevision.Should().Be(1);
        command.QueueNeighborId.Should().Be(Guid.Parse(Neighbor));
        command.QueueNeighborRevision.Should().Be(2);
        command.QueueMoveReviewId.Should().Be(verb is "move-confirm" ? Guid.Parse(Review) : null);
        SessionCommand.Parse("queue move-cancel " + Session + " " + Review, "Kora")!.Operation.Should().Be(SessionCommandOperation.QueueMoveCancel);
    }

    [Fact]
    public void UnknownInjectedUnconfirmedMalformedOrRetargetingArgumentsAreRejected()
    {
        foreach (var input in new[]
        {
            "queue move-up " + Pair + " confirm", "queue move-up " + Pair + " front",
            "queue move-up " + Pair.Replace(" 1 0 ", " 0 0 ", StringComparison.Ordinal),
            "queue move-up " + Pair.Replace(" 1 0 ", " 1 -1 ", StringComparison.Ordinal),
            "queue move-up " + Pair.Replace(" 0 " + Task, " -1 " + Task, StringComparison.Ordinal),
            "queue move-up " + Pair.Replace(Task + " 1", Task + " 0", StringComparison.Ordinal),
            "queue move-up " + Pair.Replace(Neighbor + " 2", Neighbor + " 0", StringComparison.Ordinal),
            "queue move-up " + Pair.Replace(Neighbor, Task, StringComparison.Ordinal),
            "queue move-up " + Pair.Replace(Run, "00000000-0000-0000-0000-000000000000", StringComparison.Ordinal),
            "queue move-up " + Pair.Replace(Neighbor, "chosen-label", StringComparison.Ordinal),
            "queue move-up " + Pair.Replace(Run, "{" + Run + "}", StringComparison.Ordinal),
            "queue move-confirm " + Pair + " " + Review, "queue move-confirm " + Pair + " " + Review + " unconfirmed",
            "queue move-confirm " + Pair + " bad confirm", "queue move-confirm " + Pair + " " + Review + " confirm --all",
            "queue move-cancel " + Session, "queue move-cancel " + Session + " bad",
            "queue move-cancel " + Session + " " + Review + " confirm",
        })
        {
            SessionCommand.Parse(input, "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid, input);
        }
        SessionCommand.QueueSyntax.Should().Contain("move-up|move-down").And.Contain("neighbor-revision").And.Contain("separate confirmation");
    }
}
