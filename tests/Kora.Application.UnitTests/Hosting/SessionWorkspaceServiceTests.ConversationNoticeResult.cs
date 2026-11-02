using AwesomeAssertions;

using Kora.Application.Interaction;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Interaction.ConversationNotices;
using Kora.Core.Presentation;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Fact]
    public async Task NoticeResultResolvesItsCanonicalTaskRequestNotNewestMessageOrExecutionAuthority()
    {
        using var f = new ConversationFixture();
        var (snapshot, notice, original) = NoticeResult(f);
        f.Messages = [original, original with { Request = HostRequest.Create(RequestOrigin.LocalUi), Sequence = 2 }];
        f.OverviewReader = (_, _) => Task.FromResult(snapshot);
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var content = await f.Service.ReadConversationNoticeResultAsync(snapshot.Session, notice, () => true, f.Token);
        content.Source.Should().Be(original.AssistantResponse!.Body);
        content.ConversationMessage.Should().Be(original);
        content.Origin.Should().Be(DetailContentOrigin.RetainedAssistant);
        content.Provenance.Should().Contain("Historical").And.Contain("not current execution");
        snapshot.Tasks.Single().CurrentSource.Should().BeFalse();
        f.ExactReads.Should().Be(3);
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(ModelTurnOutcome.Unavailable)]
    [InlineData(ModelTurnOutcome.Unknown)]
    [InlineData(ModelTurnOutcome.Denied)]
    public async Task NoticeResultWithoutAssistantIsExplicitlyUnavailableNotOriginalUserOrEmptySuccess(ModelTurnOutcome outcome)
    {
        using var f = new ConversationFixture();
        var (snapshot, notice, _) = NoticeResult(f, outcome);
        f.OverviewReader = (_, _) => Task.FromResult(snapshot);
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        await f.Service.Invoking(service => service.ReadConversationNoticeResultAsync(snapshot.Session, notice, () => true, f.Token))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*Unavailable*no retained ASSISTANT*typed HOST*");
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("category")]
    [InlineData("done")]
    [InlineData("session")]
    [InlineData("generation")]
    [InlineData("snapshot")]
    [InlineData("missing")]
    [InlineData("projection")]
    [InlineData("question")]
    [InlineData("pending")]
    [InlineData("source")]
    [InlineData("source-session")]
    [InlineData("task")]
    [InlineData("subject")]
    [InlineData("notice-generation")]
    [InlineData("task-generation")]
    [InlineData("revision")]
    [InlineData("related")]
    [InlineData("origin")]
    [InlineData("expiry")]
    [InlineData("type")]
    [InlineData("invalid")]
    public async Task NoticeResultRejectsStaleOrForeignSourceWithoutGuessing(string fault)
    {
        using var f = new ConversationFixture();
        var (snapshot, notice, _) = NoticeResult(f);
        var target = snapshot.Session;
        var task = snapshot.Tasks.Single();
        if (fault is "category")
        {
            notice = notice with { Source = LocalEventSource.ConversationQuestion, Type = LocalEventType.UserAttention,
                Id = LocalEvent.Identity(LocalEventSource.ConversationQuestion, notice.SessionId.Value, notice.SubjectId) };
        }
        if (fault is "done") { target = target with { Authority = target.Authority with { IsActive = false } }; }
        if (fault is "session") { target = target with { Authority = target.Authority with { SessionId = new(Guid.NewGuid()) } }; }
        if (fault is "generation") { target = target with { Authority = target.Authority with { Generation = new(2) } }; }
        if (fault is "snapshot") { snapshot = snapshot with { AuthorityRevision = 42, Session = new(f.Session, new(f.Session.SessionId, new(1), new("same title"))) }; }
        if (fault is "missing") { snapshot = snapshot with { Tasks = [] }; }
        if (fault is "projection") { task = task with { ConversationNotice = null }; }
        if (fault is "question") { task = task with { ConversationNotice = task.ConversationNotice! with { Question = new(task.Task.Request, new(Guid.NewGuid()), new(1)) } }; }
        if (fault is "pending") { task = task with { ConversationNotice = task.ConversationNotice! with { Outcome = null } }; }
        if (fault is "source") { task = task with { Source = "not authoritative" }; }
        if (fault is "source-session")
        {
            var request = task.Task.Request;
            var foreign = new HostRequest(request.RequestId, new(Guid.NewGuid()), request.TaskId, request.Origin);
            task = task with { Task = new(foreign, task.Task.Revision, task.Task.State),
                ConversationNotice = task.ConversationNotice! with { Request = foreign } };
        }
        if (fault is "task") { notice = notice with { TaskId = new(Guid.NewGuid()) }; }
        if (fault is "subject") { notice = notice with { SubjectId = Guid.NewGuid() }; notice = notice with { Id = LocalEvent.Identity(notice.Source, notice.SessionId.Value, notice.SubjectId) }; }
        if (fault is "notice-generation") { task = task with { ConversationNotice = task.ConversationNotice! with { Generation = new(2) } }; }
        if (fault is "task-generation") { task = task with { Generation = new(2) }; }
        if (fault is "revision") { notice = notice with { SourceRevision = 4 }; }
        if (fault is "related") { notice = notice with { RelatedRevision = 4 }; }
        if (fault is "origin") { notice = notice with { Origin = RequestOrigin.ActivatedVoice }; }
        if (fault is "expiry") { notice = notice with { ExpiresAt = notice.ExpiresAt.AddTicks(1) }; }
        if (fault is "type") { notice = notice with { Type = LocalEventType.Failed }; }
        if (fault is "invalid") { notice = notice with { Revision = 0 }; }
        if (fault is not "missing") { snapshot = snapshot with { Tasks = [task] }; }
        f.OverviewReader = (_, _) => Task.FromResult(snapshot);
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        await f.Service.Invoking(service => service.ReadConversationNoticeResultAsync(target, notice, () => true, f.Token))
            .Should().ThrowAsync<Exception>();
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("admission")]
    [InlineData("request")]
    [InlineData("generation")]
    [InlineData("revision")]
    [InlineData("submitted")]
    [InlineData("turn")]
    [InlineData("outcome")]
    [InlineData("reason")]
    [InlineData("final-admission")]
    [InlineData("final-source")]
    [InlineData("cancel")]
    public async Task NoticeResultRevalidatesMessageAndTerminalAfterAwaitedReads(string fault)
    {
        using var f = new ConversationFixture();
        var (snapshot, notice, message) = NoticeResult(f);
        var admitted = fault is not "admission";
        var reads = 0;
        f.OverviewReader = (_, _) =>
        {
            if (++reads == 2)
            {
                if (fault is "final-admission") { admitted = false; }
                if (fault is "final-source")
                {
                    snapshot = snapshot with { Tasks = [snapshot.Tasks.Single() with
                    { ConversationNotice = snapshot.Tasks.Single().ConversationNotice! with { Turn = new(Guid.NewGuid()) } }] };
                }
                if (fault is "cancel") { throw new OperationCanceledException(); }
            }
            return Task.FromResult(snapshot);
        };
        if (fault is "request")
        {
            var request = new HostRequest(message.Request.RequestId, message.Request.SessionId, new(Guid.NewGuid()), message.Request.Origin);
            var execution = message.Execution! with { Request = request };
            message = message with { Request = request, Execution = execution, AssistantResponse = new(execution, "foreign answer") };
        }
        if (fault is "generation") { message = message with { Generation = new(2) }; }
        if (fault is "revision") { message = message with { Revision = 4 }; }
        if (fault is "submitted") { message = message with { SubmittedAt = message.SubmittedAt.AddTicks(1) }; }
        if (fault is "turn")
        {
            var execution = message.Execution! with { Turn = new(Guid.NewGuid()) };
            message = message with { Execution = execution, AssistantResponse = new(execution, "other turn") };
        }
        if (fault is "outcome") { message = message with { Outcome = ModelTurnOutcome.Unknown, AssistantResponse = null }; }
        if (fault is "reason") { message = message with { Reason = ModelTurnReason.InvalidProviderResponse }; }
        f.Messages = [message];
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        await f.Service.Invoking(service => service.ReadConversationNoticeResultAsync(snapshot.Session, notice, () => admitted, f.Token))
            .Should().ThrowAsync<Exception>();
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
    }

    private static (SessionWorkSnapshot Snapshot, LocalEvent Event, ConversationMessage Message) NoticeResult(
        ConversationFixture f, ModelTurnOutcome outcome = ModelTurnOutcome.Succeeded)
    {
        var original = Message(f, 1, "original USER, never a fallback answer");
        var execution = new ModelTurnProvenance(new(Guid.NewGuid()), original.Request, ModelProviderSelection.OllamaCandidate);
        var message = original with { Version = 2, Revision = 3, Execution = execution, ExecutionMetadataRevision = 0,
            Outcome = outcome, Reason = ModelTurnReason.ProviderOutcome,
            AssistantResponse = outcome == ModelTurnOutcome.Succeeded ? new(execution, "# Complete retained answer\n\nExact immutable ending.") : null };
        f.Messages = [message];
        var task = new HostTaskRecord(message.Request, new(3), message.ExpectedTaskState);
        var source = new ConversationNoticeAuthority(message.Request, message.Generation, message.Revision,
            execution.Turn, message.SubmittedAt, message.Outcome, message.Reason, null, null);
        var snapshot = OverviewSnapshot(new(f.Session, null)) with
        { Tasks = [new(task, message.Generation, ConversationNoticeAuthority.Source, false, null) { ConversationNotice = source }] };
        var notice = AuthorityLocalEventSource.ConversationEvents(snapshot, message.SubmittedAt, DateTimeOffset.MinValue).Single();
        return (snapshot, notice, message);
    }
}
