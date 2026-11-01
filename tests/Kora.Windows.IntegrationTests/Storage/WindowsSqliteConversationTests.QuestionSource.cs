using AwesomeAssertions;

using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed partial class WindowsSqliteConversationTests
{
    [Theory]
    [InlineData("exact", true)]
    [InlineData("zero", true)]
    [InlineData("text", true)]
    [InlineData("form", true)]
    [InlineData("multi-choice", true)]
    [InlineData("optional-choice", true)]
    [InlineData("draft", true)]
    [InlineData("answered", true)]
    [InlineData("expired", false)]
    [InlineData("multiple", false)]
    [InlineData("purpose", false)]
    [InlineData("source", false)]
    [InlineData("unsupported-shape", false)]
    [InlineData("wrong-options", false)]
    [InlineData("approval", false)]
    [InlineData("restart", false)]
    [InlineData("other-task", false)]
    [InlineData("other-session", true)]
    [InlineData("question-generation", false)]
    public async Task ConversationCurrentSourceAdmitsOnlyItsOneLiveOrdinaryClarificationAndPreservesOtherBlockers(
        string scenario, bool current)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        var request = Fresh(f.Request.SessionId);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        var expiry = f.Time.Now.AddSeconds(15);
        using (var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
        {
            await f.Store.RetainConversationMessageAsync(request, new(1), 0, "exact original question source", static () => true, f.Token);
            await f.Store.BeginConversationExecutionAsync(request, provenance, 0, static () => true, f.Token);
            await f.Store.PublishTrustedSnapshotAsync(request, new(true, true, false, true), null, 0, f.Token);
            if (scenario is not ("approval" or "zero"))
            {
                var spec = LocalModelQuestionPolicy.CreateSpec(new("Choose format",
                    scenario is "text" ? [] : ["Compact", "Detailed"],
                    Minimum: scenario is "optional-choice" ? 0 : 1,
                    Maximum: scenario is "multi-choice" or "optional-choice" ? 2 : 1));
                if (scenario is "form")
                {
                    spec = LocalModelQuestionPolicy.CreateSpec(ModelToolProtocol.ParseAskUser(
                        """{"prompt":"Describe the answer","fields":[{"label":"Audience","type":"text","maximumLength":100,"maximumUtf8Bytes":200}]}"""u8,
                        "ask_1", formsAllowed: true).Question);
                }
                if (scenario is "purpose" or "source")
                {
                    spec = new(spec.Text, spec.Kind, spec.Options,
                        purpose: scenario is "purpose" ? "unrelated-question" : spec.Purpose,
                        sourceId: scenario is "source" ? "unrelated-source" : spec.SourceId);
                }
                if (scenario is "unsupported-shape" or "wrong-options")
                {
                    spec = new(spec.Text, scenario is "unsupported-shape" ? QuestionKind.MultipleChoice : spec.Kind,
                        scenario is "wrong-options" ? [new("foreign-a", "Compact"), new("foreign-b", "Detailed")] : spec.Options,
                        purpose: spec.Purpose, sourceId: spec.SourceId);
                }
                var question = (await f.Questions.CreateAsync(request, spec, expiry, f.Token)).Question!;
                if (scenario is "multiple") { await f.Questions.CreateAsync(request, spec, expiry, f.Token); }
                if (scenario is "draft") { await f.Questions.DraftAsync(question.Key, new(["option-0"]), RequestOrigin.LocalUi, f.Token); }
                if (scenario is "answered") { await f.Questions.SubmitAsync(question.Key, new(["option-0"]), RequestOrigin.LocalUi, f.Token); }
                if (scenario is "question-generation")
                {
                    var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.SecurityApproval,
                        "question.fixture-generation", SecurityAuditOutcome.Succeeded, SecurityAuditInitiator.System,
                        request.TaskId.Value.ToString("D"));
                    using var audited = HostActivity.BeginAudit(request, audit);
                    await f.Store.TransactAsync(request, snapshot =>
                    {
                        var stale = question with { Key = question.Key.Next(), SessionGeneration = new(2) };
                        return new(snapshot with { Questions = [stale] },
                            new(HostInteractionOutcome.Presented, "fixture-stale-generation", stale), audit);
                    }, f.Token);
                }
                question.Key.Request.Should().Be(request);
            }
        }
        if (scenario is "approval")
        {
            f.Request = new(new(Guid.NewGuid()), request.SessionId, new(Guid.NewGuid()), RequestOrigin.LocalUi, new(Guid.NewGuid()));
            await f.AdmitAsync(newSession: false);
            await f.PresentAsync();
        }
        if (scenario is "other-task" or "other-session")
        {
            f.Request = InteractionStorageFixture.NewRequest(scenario is "other-task" ? request.SessionId : null);
            await f.AdmitAsync(newSession: scenario is "other-session");
            var spec = LocalModelQuestionPolicy.CreateSpec(new("Foreign task question", ["Compact", "Detailed"]));
            await f.RunAsync(() => f.Questions.CreateAsync(f.Request, spec, expiry, f.Token));
        }
        if (scenario is "expired") { f.Time.Now = expiry; }
        if (scenario is "restart")
        {
            f.Reopen();
            await f.Store.InitializeAsync(f.Token);
        }
        var observation = (await f.Store.ReadTaskAsync(request.SessionId, request.TaskId, f.Token))!;
        observation.Task.Request.Should().Be(request);
        observation.Task.Revision.Value.Should().Be(2);
        observation.Task.State.Should().Be(HostTaskState.DispatchRecorded);
        observation.Question.Should().BeNull("this is not authority to answer through the task observation");
        observation.CurrentSource.Should().Be(current);
    }

    [Theory]
    [InlineData("task-origin")]
    [InlineData("task-request")]
    [InlineData("task-invocation")]
    [InlineData("task-generation")]
    [InlineData("task-revision")]
    [InlineData("task-state")]
    [InlineData("question-origin")]
    [InlineData("question-task")]
    [InlineData("question-request")]
    [InlineData("question-session")]
    [InlineData("question-invocation")]
    public async Task CorruptedQuestionOrTaskLineageCannotResolveToCurrentConversationSource(string fault)
    {
        using var f = new InteractionStorageFixture();
        await f.InitializeAsync();
        await FinishInitial(f);
        var request = Fresh(f.Request.SessionId);
        var provenance = new ModelTurnProvenance(new(Guid.NewGuid()), request, ModelProviderSelection.OllamaCandidate);
        using (var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
        {
            await f.Store.RetainConversationMessageAsync(request, new(1), 0, "exact source", static () => true, f.Token);
            await f.Store.BeginConversationExecutionAsync(request, provenance, 0, static () => true, f.Token);
            await f.Store.PublishTrustedSnapshotAsync(request, new(true, true, false, true), null, 0, f.Token);
            var spec = LocalModelQuestionPolicy.CreateSpec(new("Choose format", ["Compact", "Detailed"]));
            await f.Questions.CreateAsync(request, spec, f.Time.Now.AddSeconds(15), f.Token);
        }
        using (var connection = f.OpenRaw())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = fault switch
            {
                "task-origin" => "UPDATE host_tasks SET origin=$value WHERE task_id=$task;",
                "task-request" => "UPDATE host_tasks SET request_id=$value WHERE task_id=$task;",
                "task-invocation" => "UPDATE host_tasks SET invocation_id=$value WHERE task_id=$task;",
                "task-generation" => "UPDATE work_sessions SET generation=2 WHERE session_id=$session;",
                "task-revision" => "UPDATE host_tasks SET revision=3 WHERE task_id=$task;",
                "task-state" => "UPDATE host_tasks SET state=2 WHERE task_id=$task;",
                "question-origin" => "UPDATE host_questions SET payload=json_set(payload,'$.Key.Request.Origin',1) WHERE request_id=$request;",
                "question-task" => "UPDATE host_questions SET payload=json_set(payload,'$.Key.Request.TaskId.Value',$value) WHERE request_id=$request;",
                "question-request" => "UPDATE host_questions SET payload=json_set(payload,'$.Key.Request.RequestId.Value',$value) WHERE request_id=$request;",
                "question-session" => "UPDATE host_questions SET payload=json_set(payload,'$.Key.Request.SessionId.Value',$value) WHERE request_id=$request;",
                _ => "UPDATE host_questions SET payload=json_set(payload,'$.Key.Request.InvocationId',json_object('Value',$value)) WHERE request_id=$request;",
            };
            command.Parameters.AddWithValue("$value", fault is "task-origin"
                ? ((int)RequestOrigin.ActivatedVoice).ToString(System.Globalization.CultureInfo.InvariantCulture) : Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue("$task", request.TaskId.Value.ToString("D"));
            command.Parameters.AddWithValue("$session", request.SessionId.Value.ToString("D"));
            command.Parameters.AddWithValue("$request", request.RequestId.Value.ToString("D"));
            command.ExecuteNonQuery().Should().Be(1);
        }
        var failure = await Record.ExceptionAsync(async () =>
        {
            var observation = await f.Store.ReadTaskAsync(request.SessionId, request.TaskId, f.Token);
            (observation?.CurrentSource ?? false).Should().BeFalse();
        });
        if (failure is not null) { failure.Should().BeOfType<InvalidDataException>(); }
    }
}
