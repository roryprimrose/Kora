using System.Text;

using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Core.UnitTests.Commands;

public sealed class TaskCommandTests
{
    private const string Session = "11111111-1111-1111-1111-111111111111";
    private const string Task = "22222222-2222-2222-2222-222222222222";
    private const string Question = "33333333-3333-3333-3333-333333333333";

    [Theory]
    [InlineData("task status", SessionCommandOperation.TaskStatus)]
    [InlineData("task inspect", SessionCommandOperation.TaskInspect)]
    public void Exact_task_observation_uses_existing_grammar_and_configured_prefix(string operation, SessionCommandOperation expected)
    {
        var command = SessionCommand.Parse("Echo, " + operation + " " + Session + " " + Task, "Echo")!;
        command.Operation.Should().Be(expected);
        command.SessionId.Should().Be(Guid.Parse(Session));
        command.TaskId.Should().Be(Guid.Parse(Task));
        SessionCommand.Parse("task help", "Kora")!.Operation.Should().Be(SessionCommandOperation.Help);
    }

    [Fact]
    public void Cancellation_preserves_every_exact_conflict_token_and_whole_input_boundary()
    {
        var text = $"task cancel {Session} {Task} 1 2 {Question} 3";
        var exact = text.PadRight(SessionCommand.MaximumInputBytes);
        Encoding.UTF8.GetByteCount(exact).Should().Be(SessionCommand.MaximumInputBytes);
        var command = SessionCommand.Parse(exact, "Kora")!;
        command.Operation.Should().Be(SessionCommandOperation.TaskCancel);
        command.Generation.Should().Be(2);
        command.TaskRevision.Should().Be(1);
        command.QuestionId.Should().Be(Guid.Parse(Question));
        command.QuestionRevision.Should().Be(3);
        SessionCommand.Parse(exact + " ", "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid);
        var target = new HostTaskCancellationTarget(new(Guid.Parse(Session)), new(Guid.Parse(Task)), new(1), new(2),
            new(Guid.Parse(Question)), new(3));
        target.SessionId.Value.Should().Be(command.SessionId!.Value);
        target.TaskId.Value.Should().Be(command.TaskId!.Value);
        target.TaskRevision.Value.Should().Be(command.TaskRevision);
        target.Generation.Value.Should().Be(command.Generation);
        target.QuestionId.Value.Should().Be(command.QuestionId!.Value);
        target.QuestionRevision.Value.Should().Be(command.QuestionRevision);
    }

    [Theory]
    [InlineData("task")]
    [InlineData("task help extra")]
    [InlineData("task\tstatus")]
    [InlineData("taskflow")]
    [InlineData("task status same-title")]
    [InlineData("task status {session}")]
    [InlineData("task status nope {task}")]
    [InlineData("task inspect {session} nope")]
    [InlineData("task status {session} {task} extra")]
    [InlineData("task unknown {session} {task}")]
    [InlineData("task cancel {session} {task} 0 1 {question} 1")]
    [InlineData("task cancel {session} {task} 1 0 {question} 1")]
    [InlineData("task cancel {session} {task} 1 1 nope 1")]
    [InlineData("task cancel {session} {task} 1 1 {question} 0")]
    [InlineData("task cancel {session} {task} 1 1 {question} 1 extra")]
    public void Unknown_incomplete_or_ambiguous_task_shapes_fail_locally(string text)
    {
        text = text.Replace("{session}", Session, StringComparison.Ordinal)
            .Replace("{task}", Task, StringComparison.Ordinal).Replace("{question}", Question, StringComparison.Ordinal);
        if (text is "taskflow") { SessionCommand.Parse(text, "Kora").Should().BeNull(); }
        else { SessionCommand.Parse(text, "Kora")!.Operation.Should().Be(SessionCommandOperation.Invalid); }
    }

    [Fact]
    public void Local_version_shape_is_content_validation_not_standalone_admission_authority()
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        HostQuestionRecord Record(QuestionSpec spec) => new(new(request, new(Guid.NewGuid()), new(1)),
            spec, new(1), DateTimeOffset.MaxValue);
        var question = Record(LocalVersionWait.CreateSpec());
        LocalVersionWait.Source.Should().Be("host.local-version.v1");
        LocalVersionWait.Matches(question).Should().BeTrue();
        LocalVersionWait.Matches(Record(new("Other", QuestionKind.Text, [], maximumTextLength: 20))).Should().BeFalse();
        LocalVersionWait.Matches(Record(new("Other", QuestionKind.SingleChoice, [new("show", "Show")]))).Should().BeFalse();
        LocalVersionWait.Matches(Record(new("Other", QuestionKind.SingleChoice, [new("show", "Show")],
            purpose: "local-version", sourceId: "other"))).Should().BeFalse();
        LocalVersionWait.Matches(Record(new("Other", QuestionKind.SingleChoice, [new("show", "Show"), new("no", "No")],
            purpose: "local-version"))).Should().BeFalse();
        LocalVersionWait.Matches(Record(new("Other", QuestionKind.SingleChoice, [new("other", "Other")],
            purpose: "local-version"))).Should().BeFalse();
        var observation = new HostTaskObservation(new(request, new(1), HostTaskState.IntentRecorded),
            new(1), LocalVersionWait.Source, false, question);
        var json = SessionCommandResult.Serialize(new("observed", "Bounded") { TaskDetails = [observation] });
        Encoding.UTF8.GetString(json).Should().Contain("local-version").And.Contain(request.TaskId.Value.ToString("D"));
    }
}
