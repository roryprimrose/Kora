using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Theory]
    [InlineData("help")]
    [InlineData("list")]
    [InlineData("status")]
    [InlineData("inspect")]
    [InlineData("questions")]
    [InlineData("create")]
    [InlineData("rename")]
    [InlineData("done")]
    [InlineData("resume")]
    public async Task Every_admitted_command_owns_fresh_original_user_intent_and_structured_exact_result(string verb)
    {
        using var fixture = new Fixture();
        var id = fixture.Request.SessionId.Value.ToString("D");
        var text = verb switch
        {
            "status" or "inspect" => "session " + verb + " " + id,
            "questions" => "session inspect " + id + " questions",
            "create" => "session create \"User label\"",
            "rename" => "session rename " + id + " 1 0 \"User label\"",
            "done" or "resume" => "session " + verb + " " + id + " 1",
            _ => "session " + verb,
        };
        var result = await fixture.Service.ExecuteCommandAsync(SessionCommand.Parse(text, "Kora")!,
            RequestOrigin.ActivatedVoice, () => true, fixture.Token);
        fixture.TaskWrites.Should().HaveCount(2);
        fixture.TaskWrites[0].Request.RequestId.Should().NotBe(fixture.Request.RequestId);
        fixture.TaskWrites[0].Request.TaskId.Should().NotBe(fixture.Request.TaskId);
        fixture.TaskWrites[0].Request.Origin.Should().Be(RequestOrigin.ActivatedVoice);
        fixture.TaskWrites[1].State.Should().Be(HostTaskState.Succeeded);
        if (verb is "help") {         result.Message.Should().Be(SessionCommand.Syntax + " " + SessionCommand.TaskSyntax + " " + SessionCommand.QueueSyntax); }
        else { result.Sessions.Should().ContainSingle(); }
        if (verb is "done" or "resume")
        {
            result.Sessions[0].MetadataRevision.Should().BeNull();
            result.Sessions[0].Generation.Should().Be(2);
        }
        if (verb is "inspect") { result.Tasks.Should().ContainSingle().Which.Id.Should().Be(fixture.Task.Request.TaskId.Value); }
        if (verb is "questions") { result.Questions.Should().ContainSingle().Which.Id.Should().Be(fixture.Question.Key.QuestionId.Value); }
        SessionCommandResult.Serialize(result).Length.Should().BeLessThanOrEqualTo(SessionCommand.MaximumResultBytes);
    }

    [Theory]
    [InlineData(SessionCommandOperation.TaskStatus, false)]
    [InlineData(SessionCommandOperation.TaskInspect, false)]
    [InlineData(SessionCommandOperation.TaskInspect, true)]
    public async Task Exact_task_reads_distinguish_unknown_and_complete_inspection_without_mutation(SessionCommandOperation operation, bool unknown)
    {
        using var fixture = new Fixture { UnknownTask = unknown, CanControl = false };
        var result = await fixture.Service.ExecuteCommandAsync(new(operation, fixture.Request.SessionId.Value)
        { TaskId = fixture.Request.TaskId.Value }, RequestOrigin.ActivatedVoice, () => true, fixture.Token);
        result.Outcome.Should().Be(unknown ? "unknown" : "observed");
        if (!unknown)
        {
            result.TaskDetails.Single().Task.Should().Be(fixture.Task);
            result.TaskDetails.Single().Question.Should().Be(fixture.Question);
        }
        fixture.ControlCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exact_command_and_native_cancellation_share_fresh_user_control_and_notification(bool command)
    {
        using var fixture = new Fixture { CanControl = false };
        HostTaskObservation? notified = null;
        fixture.Service.WaitingTaskCancelled += observation => notified = observation;
        HostTaskObservation cancelled;
        if (command)
        {
            var result = await fixture.Service.ExecuteCommandAsync(new(SessionCommandOperation.TaskCancel, fixture.Request.SessionId.Value, 1)
            {
                TaskId = fixture.Request.TaskId.Value,
                TaskRevision = 1,
                QuestionId = fixture.Question.Key.QuestionId.Value,
                QuestionRevision = 1,
            }, RequestOrigin.ActivatedVoice, () => true, fixture.Token);
            cancelled = result.TaskDetails.Single();
        }
        else
        {
            cancelled = await fixture.Service.CancelTaskAsync(new(fixture.Request.SessionId, fixture.Request.TaskId,
                new(1), new(1), fixture.Question.Key.QuestionId, new(1)), RequestOrigin.LocalUi, () => true, fixture.Token);
        }
        notified.Should().Be(cancelled);
        cancelled.Task.State.Should().Be(HostTaskState.Cancelled);
        fixture.TaskWrites[0].Request.RequestId.Should().NotBe(fixture.Request.RequestId);
        fixture.TaskWrites.Last().State.Should().Be(HostTaskState.Succeeded);
    }

    [Theory]
    [InlineData(SessionCommandOperation.TaskStatus, false)]
    [InlineData(SessionCommandOperation.TaskCancel, false)]
    [InlineData(SessionCommandOperation.TaskCancel, true)]
    public async Task Programmatic_commands_cannot_omit_exact_task_or_question_ids(SessionCommandOperation operation, bool supplyTask)
    {
        using var fixture = new Fixture();
        var act = () => fixture.Service.ExecuteCommandAsync(new(operation, fixture.Request.SessionId.Value, 1)
        { TaskId = supplyTask ? fixture.Request.TaskId.Value : null, TaskRevision = 1 },
            RequestOrigin.LocalUi, () => true, fixture.Token);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("private")]
    [InlineData("late")]
    public async Task Task_subject_is_host_resolved_before_any_fresh_intent_not_fabricated_from_unknown_input(string scenario)
    {
        using var fixture = new Fixture
        {
            ForeignTaskSession = scenario is "foreign",
            CanInspect = scenario is not "private",
            RevokeDuringTaskResolution = scenario is "late",
        };
        var command = new SessionCommand(SessionCommandOperation.TaskInspect,
            scenario is "missing" ? null : fixture.Request.SessionId.Value) { TaskId = fixture.Request.TaskId.Value };
        var execute = () => fixture.Service.ExecuteCommandAsync(command, RequestOrigin.LocalUi, () => true, fixture.Token);
        await execute.Should().ThrowAsync<Exception>();
        fixture.TaskWrites.Should().BeEmpty();
        fixture.ControlCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Confirmed_cancellation_is_not_relabelled_when_notification_cancels_caller_token(bool command)
    {
        using var fixture = new Fixture();
        using var token = new CancellationTokenSource();
        fixture.Service.WaitingTaskCancelled += _ => token.Cancel();
        if (command)
        {
            var result = await fixture.Service.ExecuteCommandAsync(new(SessionCommandOperation.TaskCancel, fixture.Request.SessionId.Value, 1)
            {
                TaskId = fixture.Request.TaskId.Value,
                TaskRevision = 1,
                QuestionId = fixture.Question.Key.QuestionId.Value,
                QuestionRevision = 1,
            }, RequestOrigin.LocalUi, () => true, token.Token);
            result.Outcome.Should().Be("committed");
            result.TaskDetails.Single().Task.State.Should().Be(HostTaskState.Cancelled);
        }
        else
        {
            var result = await fixture.Service.CancelTaskAsync(new(fixture.Request.SessionId, fixture.Request.TaskId,
                new(1), new(1), fixture.Question.Key.QuestionId, new(1)), RequestOrigin.LocalUi, () => true, token.Token);
            result.Task.State.Should().Be(HostTaskState.Cancelled);
        }
        fixture.TaskWrites.Last().State.Should().Be(HostTaskState.Succeeded);
    }

    [Fact]
    public async Task Reads_use_private_inspection_not_mutation_permission_and_do_not_change_session_authority()
    {
        using var fixture = new Fixture { CanControl = false };
        var result = await fixture.Service.ExecuteCommandAsync(new(SessionCommandOperation.Status, fixture.Request.SessionId.Value),
            RequestOrigin.LocalUi, () => true, fixture.Token);
        result.Outcome.Should().Be("observed");
        result.Sessions[0].Generation.Should().Be(1);
        fixture.ControlCalls.Should().Be(0);
    }

    [Fact]
    public async Task Cancellation_without_an_open_presenter_still_returns_the_authoritative_result()
    {
        using var fixture = new Fixture();
        var result = await fixture.Service.CancelTaskAsync(new(fixture.Request.SessionId, fixture.Request.TaskId,
            new(1), new(1), fixture.Question.Key.QuestionId, new(1)), RequestOrigin.LocalUi, () => true, fixture.Token);
        result.Task.State.Should().Be(HostTaskState.Cancelled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Original_channel_admission_is_checked_before_intent_and_after_observation(bool revokeAfter)
    {
        using var fixture = new Fixture();
        var calls = 0;
        var act = () => fixture.Service.ExecuteCommandAsync(new(SessionCommandOperation.Help),
            RequestOrigin.LocalUi, () => ++calls <= (revokeAfter ? 1 : 0), fixture.Token);
        await act.Should().ThrowAsync<InvalidOperationException>();
        fixture.TaskWrites.Should().NotContain(record => record.State == HostTaskState.Succeeded);
        if (revokeAfter) { fixture.TaskWrites.Last().State.Should().Be(HostTaskState.Denied); }
        else { fixture.TaskWrites.Should().BeEmpty(); }
    }

    [Theory]
    [InlineData(SessionCommandOperation.Invalid)]
    [InlineData((SessionCommandOperation)999)]
    public async Task Unsupported_typed_commands_fail_explicitly(SessionCommandOperation operation)
    {
        using var fixture = new Fixture();
        var act = () => fixture.Service.ExecuteCommandAsync(new(operation, Error: "Explicit invalid grammar"),
            RequestOrigin.LocalUi, () => true, fixture.Token);
        await act.Should().ThrowAsync<InvalidOperationException>();
        fixture.TaskWrites.Should().NotContain(record => record.State == HostTaskState.Succeeded);
    }

    [Theory]
    [InlineData(SessionCommandOperation.Create)]
    [InlineData(SessionCommandOperation.Rename)]
    public async Task Missing_validated_names_cannot_mutate(SessionCommandOperation operation)
    {
        using var fixture = new Fixture();
        var act = () => fixture.Service.ExecuteCommandAsync(new(operation, fixture.Request.SessionId.Value, 1),
            RequestOrigin.LocalUi, () => true, fixture.Token);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*quoted name*");
    }

    [Fact]
    public async Task Unknown_inspection_page_and_cancelled_read_never_report_success()
    {
        using var fixture = new Fixture();
        var act = () => fixture.Service.ExecuteCommandAsync(
            new(SessionCommandOperation.Inspect, fixture.Request.SessionId.Value) { PageKind = "approval" },
            RequestOrigin.LocalUi, () => true, fixture.Token);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*tasks or questions*");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var read = () => fixture.Service.ExecuteCommandAsync(new(SessionCommandOperation.Help),
            RequestOrigin.LocalUi, () => true, cancelled.Token);
        await read.Should().ThrowAsync<OperationCanceledException>();
        fixture.TaskWrites.Should().NotContain(record => record.State == HostTaskState.Succeeded);
    }

    [Fact]
    public async Task Maximum_domain_unicode_page_that_exceeds_serialized_bound_is_rejected_not_truncated()
    {
        var name = new SessionName(string.Concat(Enumerable.Repeat("\U0001F600", SessionName.MaximumScalars)));
        var rows = Enumerable.Range(0, SessionPage<SessionWorkspaceEntry>.MaximumRecords).Select(_ =>
        {
            var id = new HostId<SessionIdentity>(Guid.NewGuid());
            return new SessionWorkspaceEntry(new(id, new(1), true), new(id, new(1), name));
        }).ToArray();
        using var fixture = new Fixture { MetadataPage = new([.. rows], null) };
        var act = () => fixture.Service.ExecuteCommandAsync(new(SessionCommandOperation.List, Limit: 50),
            RequestOrigin.LocalUi, () => true, fixture.Token);
        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*smaller explicit page*");
        fixture.TaskWrites.Should().NotContain(record => record.State == HostTaskState.Succeeded);
    }
}