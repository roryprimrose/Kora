using AwesomeAssertions;

using Kora.Core.Commands;

namespace Kora.Core.UnitTests.Commands;

public sealed class SessionQueueCommandTests
{
    private const string Session = "11111111-1111-1111-1111-111111111111";
    private const string TaskId = "22222222-2222-2222-2222-222222222222";
    private const string Request = "33333333-3333-3333-3333-333333333333";

    [Theory]
    [InlineData("queue help", SessionCommandOperation.Help)]
    [InlineData("queue list " + Session, SessionCommandOperation.QueueList)]
    [InlineData("queue status " + Session + " " + TaskId, SessionCommandOperation.QueueStatus)]
    [InlineData("queue enqueue " + Session + " 1 0 " + Request + " " + TaskId + " version", SessionCommandOperation.QueueEnqueue)]
    [InlineData("Kora, queue enqueue " + Session + " 1 0 " + Request + " " + TaskId + " version after " + Request, SessionCommandOperation.QueueEnqueue)]
    [InlineData("queue cancel " + Session + " 1 5 " + TaskId + " 1", SessionCommandOperation.QueueCancel)]
    [InlineData("queue remove " + Session + " 1 5 " + TaskId + " 1", SessionCommandOperation.QueueRemove)]
    [InlineData("queue clear " + Session + " 1 5 confirm", SessionCommandOperation.QueueClear)]
    [InlineData("queue dispatch " + Session + " 1 5", SessionCommandOperation.QueueDispatch)]
    [InlineData("queue pause-status " + Session, SessionCommandOperation.QueuePauseStatus)]
    [InlineData("queue pause " + Session + " 1 5 " + Request + " 0", SessionCommandOperation.QueuePause)]
    [InlineData("Kora, queue resume " + Session + " 1 5 " + Request + " 2", SessionCommandOperation.QueueResume)]
    public void Exact_queue_grammar_is_deterministic_without_inference(string input, SessionCommandOperation operation)
    {
        var command = SessionCommand.Parse(input, "Kora")!;
        command.Operation.Should().Be(operation);
        if (operation == SessionCommandOperation.QueueEnqueue)
        {
            command.WorkRequestId.Should().Be(Guid.Parse(Request));
            command.TaskId.Should().Be(Guid.Parse(TaskId));
            command.DependencyTaskId.Should().Be(input.Contains(" after ", StringComparison.Ordinal) ? Guid.Parse(Request) : null);
            command.ExpectedDependencyRevision.Should().BeNull("typed after-task semantics do not require a native observation guard");
        }
        if (operation is SessionCommandOperation.QueuePause or SessionCommandOperation.QueueResume)
        {
            command.QueueRunId.Should().Be(Guid.Parse(Request));
            command.QueueControlRevision.Should().Be(operation == SessionCommandOperation.QueuePause ? 0 : 2);
        }
    }

    [Theory]
    [InlineData("queue")]
    [InlineData("queue list")]
    [InlineData("queue list friendly-name")]
    [InlineData("queue status " + Session + " unknown")]
    [InlineData("queue dispatch " + Session + " 0 0")]
    [InlineData("queue dispatch " + Session + " 1 -1")]
    [InlineData("queue clear " + Session + " 1 0")]
    [InlineData("queue clear " + Session + " 1 0 yes")]
    [InlineData("queue cancel " + Session + " 1 0 unknown 1")]
    [InlineData("queue remove " + Session + " 1 0 " + TaskId + " 0")]
    [InlineData("queue enqueue " + Session + " 1 0 unknown " + TaskId + " version")]
    [InlineData("queue enqueue " + Session + " 1 0 " + Request + " unknown version")]
    [InlineData("queue enqueue " + Session + " 1 0 " + Request + " " + TaskId + " shell")]
    [InlineData("queue enqueue " + Session + " 1 0 " + Request + " " + TaskId + " version before " + Request)]
    [InlineData("queue enqueue " + Session + " 1 0 " + Request + " " + TaskId + " version after unknown")]
    [InlineData("queue enqueue " + Session + " 1 0 " + Request + " " + TaskId + " version extra")]
    [InlineData("queue list " + Session + "\n")]
    [InlineData("queue replace " + Session + " 1 0")]
    [InlineData("queue pause " + Session + " 1 0")]
    [InlineData("queue pause " + Session + " 1 0 " + Request + " -1")]
    [InlineData("queue pause " + Session + " 1 0 " + Request + " 9223372036854775808")]
    [InlineData("queue resume " + Session + " 1 0 unknown 0")]
    [InlineData("queue resume " + Session + " 1 0 00000000-0000-0000-0000-000000000000 0")]
    [InlineData("queue pause " + Session + " 1 0 " + Request + " 0 extra")]
    [InlineData("queue pause-status " + Session + " extra")]
    public void Unavailable_effects_names_stale_revisions_and_ambiguous_confirmation_are_rejected(string input) =>
        SessionCommand.Parse(input, "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);

    [Fact]
    public void Oversized_queue_input_is_not_truncated_into_a_committed_request() =>
        SessionCommand.Parse("queue " + new string('x', SessionCommand.MaximumInputBytes), "Kora")!.Operation
            .Should().Be(SessionCommandOperation.Invalid);

    [Fact]
    public void OptionalNativeGuardCarriesARealRevisionWithoutChangingTypedGrammar()
    {
        var parsed = SessionCommand.Parse("queue enqueue " + Session + " 1 0 " + Request + " " + TaskId
            + " version after " + Request, "Kora")!;
        var native = parsed with { ExpectedDependencyRevision = new(5) };
        native.ExpectedDependencyRevision!.Value.Value.Should().Be(5);
        parsed.ExpectedDependencyRevision.Should().BeNull();
        native.DependencyTaskId.Should().Be(parsed.DependencyTaskId);
    }

    [Fact]
    public void Assistant_prefix_requires_a_complete_current_name_and_explicit_boundary()
    {
        SessionCommand.Parse("Kora", "Kora").Should().BeNull();
        SessionCommand.Parse("KoraX", "Kora").Should().BeNull();
        SessionCommand.Parse("Kora queue help", "Kora")!.Operation.Should().Be(SessionCommandOperation.Help);
    }
}
