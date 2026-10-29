using System.Collections.Immutable;

using AwesomeAssertions;

using Kora.Application.Hosting;
using Kora.Core.Commands;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Hosting;

public sealed partial class SessionWorkspaceServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RemainingWorkRetainsUnknownInterruptedAndQuestionsWithoutWorkerAbsenceInferringCompletion(bool active)
    {
        using var f = new Fixture();
        f.Session = f.Session with { IsActive = active };
        var target = new SessionWorkspaceEntry(f.Session, null);
        var unknown = new HostTaskObservation(new(f.Request, new(3), HostTaskState.Unknown), new(1), "durable", false, null);
        var interrupted = unknown with
        {
            Task = new(RemainingRequest(f.Request.SessionId),
            new(2), HostTaskState.Interrupted)
        };
        var snapshot = RemainingSnapshot(target, [unknown, interrupted], omitted: 7);
        f.OverviewReader = (_, _) => Task.FromResult(snapshot);
        await using var conversation = RemainingConversation(f);
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var answer = await conversation.ReadRemainingWorkAsync(target, () => true, f.Token);
        await conversation.ValidateRemainingWorkAsync(answer, () => true, f.Token);
        var body = answer.Format();
        body.Should().Contain("Unknown").And.Contain("Interrupted").And.Contain("7 task records")
            .And.Contain("Pending questions: 1").And.Contain("no in-memory execution owner")
            .And.Contain("not an all-clear").And.Contain("No future steps, percentage, ETA")
            .And.NotContain(f.Request.SessionId.Value.ToString("D")).And.NotContain("Actual question");
        if (!active) { body.Should().Contain("Done; observed without Resume"); }
        conversation.IsRemainingWorkCurrent(answer, () => false).Should().BeFalse();
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("head")]
    [InlineData("task")]
    [InlineData("question")]
    [InlineData("metadata")]
    [InlineData("foreign-task")]
    [InlineData("foreign-question")]
    [InlineData("negative")]
    [InlineData("default")]
    [InlineData("default-questions")]
    [InlineData("default-queue")]
    [InlineData("oversized")]
    [InlineData("oversized-tasks")]
    [InlineData("negative-questions")]
    [InlineData("negative-queue")]
    [InlineData("admission")]
    [InlineData("control")]
    public async Task RemainingWorkRejectsChangedIncompleteOrCrossSessionSourcesWithoutSuccess(string fault)
    {
        using var f = new Fixture();
        var target = new SessionWorkspaceEntry(f.Session, null);
        var calls = 0;
        var snapshot = RemainingSnapshot(target);
        f.OverviewReader = (_, _) =>
        {
            calls++;
            var value = snapshot;
            if (fault is "head" && calls == 2) { value = value with { AuthorityRevision = 2 }; }
            if (fault is "task" && calls == 2)
            { value = value with { Tasks = [new(new(f.Request, new(2), HostTaskState.Unknown), new(1), "source", false, null)] }; }
            if (fault is "question" && calls == 2) { value = value with { PendingQuestions = [] }; }
            if (fault is "metadata") { value = value with { Session = target with { Metadata = new(f.Request.SessionId, new(1), new("Changed")) } }; }
            if (fault is "foreign-task")
            { value = value with { Tasks = [new(new(HostRequest.Create(RequestOrigin.LocalUi), new(1), HostTaskState.Unknown), new(1), "source", false, null)] }; }
            if (fault is "foreign-question")
            { value = value with { PendingQuestions = [value.PendingQuestions[0] with { Key = new(HostRequest.Create(RequestOrigin.LocalUi), new(Guid.NewGuid()), new(1)) }] }; }
            if (fault is "negative") { value = value with { OmittedTasks = -1 }; }
            if (fault is "default") { value = value with { Tasks = default }; }
            if (fault is "default-questions") { value = value with { PendingQuestions = default }; }
            if (fault is "default-queue") { value = value with { QueueRecords = default }; }
            if (fault is "oversized") { value = value with { PendingQuestions = Enumerable.Repeat(value.PendingQuestions[0], 51).ToImmutableArray() }; }
            if (fault is "oversized-tasks")
            { value = value with { Tasks = Enumerable.Repeat(new HostTaskObservation(f.Task, new(1), "source", true, null), 51).ToImmutableArray() }; }
            if (fault is "negative-questions") { value = value with { OmittedQuestions = -1 }; }
            if (fault is "negative-queue") { value = value with { OmittedQueueRecords = -1 }; }
            if (fault is "admission") { f.CanInspect = false; }
            if (fault is "control") { f.AdvanceRevision(); }
            return Task.FromResult(value);
        };
        await using var conversation = RemainingConversation(f);
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        await conversation.Invoking(service => service.ReadRemainingWorkAsync(target, () => true, f.Token))
            .Should().ThrowAsync<Exception>();
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
    }

    [Fact]
    public async Task FinalRemainingWorkValidationRejectsLaterDurableTransitionAndUnavailableRead()
    {
        using var f = new Fixture();
        var target = new SessionWorkspaceEntry(f.Session, null);
        var snapshot = RemainingSnapshot(target);
        f.OverviewReader = (_, _) => Task.FromResult(snapshot);
        await using var conversation = RemainingConversation(f);
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var answer = await conversation.ReadRemainingWorkAsync(target, () => true, f.Token);
        snapshot = snapshot with { AuthorityRevision = 2 };
        await conversation.Invoking(service => service.ValidateRemainingWorkAsync(answer, () => true, f.Token))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*changed*");
        f.OverviewReader = (_, _) => Task.FromException<SessionWorkSnapshot>(new IOException("unavailable"));
        await conversation.Invoking(service => service.ReadRemainingWorkAsync(target, () => true, f.Token))
            .Should().ThrowAsync<IOException>();
        await conversation.Invoking(service => service.ReadRemainingWorkAsync(target, () => false, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(ConversationExecutionPhase.Waiting)]
    [InlineData(ConversationExecutionPhase.Running)]
    [InlineData(ConversationExecutionPhase.Cancelling)]
    [InlineData(ConversationExecutionPhase.Completing)]
    [InlineData(ConversationExecutionPhase.Terminal)]
    [InlineData(ConversationExecutionPhase.Unconfirmed)]
    public void RemainingWorkFormatsOnlyObservedStepsAndDeduplicatesDispatchingOriginal(ConversationExecutionPhase phase)
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var target = new SessionWorkspaceEntry(new(request.SessionId, new(1), true), new(request.SessionId, new(1), new("Build")));
        var now = DateTimeOffset.UnixEpoch;
        var cancellation = new ConversationCancellationTarget(Guid.NewGuid(), request, new(1), new(2), now, now.AddMinutes(1));
        var observation = new ConversationWorkObservation(1, 1, 0, true, true, 1,
            [new(request, new(1), new(1), now.AddMinutes(1), ConversationPendingPhase.Dispatching, 0),
            new(RemainingRequest(request.SessionId), new(1), new(1), now.AddMinutes(1), ConversationPendingPhase.Pending, 1),
            new(RemainingRequest(request.SessionId), new(2), new(1), now, ConversationPendingPhase.Pending, 2)],
            new(cancellation, phase, Progress: phase == ConversationExecutionPhase.Running
                ? new(ConversationExecutionStep.ReadingObservation, 1, 1) : null), phase == ConversationExecutionPhase.Running);
        var answer = new SessionRemainingWorkAnswer(target, now, now, [], [], 0, 0, 0, 0)
        { Ordinary = observation, CurrentReceipt = new(request, new(1), new(2), HostTaskState.DispatchRecorded, true) };
        var body = answer.Format();
        body.Should().Contain("Conversation: Build").And.Contain("FIFO position 1").And.Contain("FIFO position 2")
            .And.Contain("expired as observed").And.Contain("earlier session source")
            .And.Contain("Ordinary dispatch: held").And.Contain("Accepting originals not yet retained: 1")
            .And.NotContain("Submitted request still owned").And.NotContain(request.RequestId.Value.ToString("D"));
        if (phase == ConversationExecutionPhase.Running)
        {
            body.Should().Contain("Actual host step: reading an admitted observation");
            (answer with
            {
                Ordinary = observation with
                {
                    Running = observation.Running! with
                    { Progress = new(ConversationExecutionStep.GeneratingAnswer, 1, 2) }
                }
            }).Format().Should().Contain("generating an answer");
            (answer with
            {
                Ordinary = observation with
                {
                    Running = observation.Running! with
                    { Progress = new(ConversationExecutionStep.GeneratingAnswer, 1, 2) }
                }
            }).FormatSpeech().Should().Contain("generating an answer");
        }
        else { body.Should().Contain("Actual host step: unavailable"); }
        answer.FormatSpeech().Should().Contain("Bounded separate observations");
        (answer with { Ordinary = observation with { Running = null } }).FormatSpeech().Should().Contain("no current execution confirmed");
        var noCurrentStep = answer with
        {
            Ordinary = observation with
            {
                Paused = false,
                Running = observation.Running! with
                { Progress = new(ConversationExecutionStep.GeneratingAnswer, 0, 0) },
                ProgressCurrent = false
            }
        };
        noCurrentStep.Format().Should().Contain("Actual host step: unavailable");
        noCurrentStep.FormatSpeech().Should().NotContain("generating an answer");
        (answer with { Ordinary = observation with { Paused = false } }).FormatSpeech().Should().Contain("dispatch held");
        (answer with
        {
            Ordinary = observation with
            {
                Running = observation.Running! with
                { Progress = new(ConversationExecutionStep.ReadingObservation, 1, 1) },
                ProgressCurrent = true
            }
        })
            .FormatSpeech().Should().Contain("reading an admitted observation");
        (answer with
        {
            Questions = [new(new(request, new(Guid.NewGuid()), new(1)), new(2), QuestionStatus.Pending,
            QuestionKind.Text, null, now)]
        }).Format().Should().Contain("expired as observed; earlier session source");
        (answer with { Unresolved = [new(RemainingRequest(request.SessionId), new(2), new(1), HostTaskState.Unknown, true)] })
            .Format().Should().Contain("earlier/unavailable source");
        (answer with { Ordinary = observation with { Running = null } }).Format().Should().Contain("Submitted request still owned");
    }

    private static SessionConversationService RemainingConversation(Fixture f) =>
        new(null!, f, f.Service, f, null!, f.QueueTime, NullLogger<SessionConversationService>.Instance);

    [Theory]
    [InlineData("tasks")]
    [InlineData("questions")]
    [InlineData("queue")]
    [InlineData("queue-omissions")]
    public async Task RemainingWorkFinalFenceIncludesEveryPublishedOmissionCount(string mismatch)
    {
        using var f = new Fixture();
        var target = new SessionWorkspaceEntry(f.Session, null);
        var snapshot = RemainingSnapshot(target);
        f.OverviewReader = (_, _) => Task.FromResult(snapshot);
        await using var conversation = RemainingConversation(f);
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var answer = await conversation.ReadRemainingWorkAsync(target, () => true, f.Token);
        answer = mismatch switch
        {
            "tasks" => answer with { OmittedTasks = 1 },
            "questions" => answer with { OmittedQuestions = 1 },
            "queue" => answer with { OtherQueueRecords = 1 },
            _ => answer with { OmittedQueueRecords = 1 },
        };
        await conversation.Invoking(service => service.ValidateRemainingWorkAsync(answer, () => true, f.Token))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("choose")]
    [InlineData("cancel")]
    [InlineData("expired")]
    [InlineData("channel")]
    [InlineData("source")]
    [InlineData("retired")]
    [InlineData("control")]
    [InlineData("low-choice")]
    [InlineData("high-choice")]
    [InlineData("publish-retired")]
    public async Task DuplicateReadableNamesRequireAnExactBoundOriginalChannelChoiceOrCancel(string ending)
    {
        using var f = new Fixture();
        var first = new SessionWorkspaceEntry(f.Session, new(f.Session.SessionId, new(1), new("Build")));
        var secondId = new HostId<SessionIdentity>(Guid.NewGuid());
        var second = new SessionWorkspaceEntry(new(secondId, new(1), false), new(secondId, new(1), new("Build")));
        var rows = new[] { first, second }.OrderBy(row => row.Authority.SessionId.Value.ToString("D"), StringComparer.Ordinal).ToImmutableArray();
        f.ListReader = (_, _) => new(rows, null);
        f.ExactListReader = id => rows.Single(row => row.Authority.SessionId == id);
        var snapshots = rows.ToDictionary(row => row.Authority.SessionId, row => RemainingSnapshot(row));
        f.OverviewReader = (id, _) => Task.FromResult(snapshots[id]);
        await using var conversation = RemainingConversation(f);
        var clock = new RemainingClock();
        var commands = new SessionRemainingWorkCommandService(f.Service, conversation, f, clock);
        var source = true;
        if (ending is "publish-retired") { clock.BeforeRead = () => source = false; }
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        var original = SessionCommand.Parse("what work remains in \"Build\"", "Kora")!;
        if (ending is "publish-retired")
        {
            await commands.Invoking(service => service.ExecuteAsync(original, RequestOrigin.LocalUi, () => source, f.Token))
                .Should().ThrowAsync<InvalidOperationException>();
            return;
        }
        var result = await commands.ExecuteAsync(original, RequestOrigin.LocalUi, () => source, f.Token);
        result.Answer.Should().BeNull();
        result.Choices.Should().HaveCount(2);
        result.Format().Should().Contain("1. Build").And.Contain("2. Build").And.Contain("session remaining cancel")
            .And.NotContain(secondId.Value.ToString("D"));
        if (ending is "expired") { clock.Now = clock.Now.AddMinutes(2); }
        if (ending is "source") { source = false; }
        if (ending is "retired") { commands.Retire(); }
        if (ending is "control") { f.AdvanceRevision(); }
        var follow = new SessionCommand(ending is "cancel" ? SessionCommandOperation.RemainingWorkCancel
            : SessionCommandOperation.RemainingWorkChoose)
        { RemainingChoice = ending is "low-choice" ? 0 : ending is "high-choice" ? 3 : 2 };
        var origin = ending is "channel" ? RequestOrigin.ActivatedVoice : RequestOrigin.LocalUi;
        if (ending is "choose" or "cancel")
        {
            var answer = await commands.ExecuteAsync(follow, origin, () => true, f.Token);
            if (ending is "choose") { answer.Answer!.Session.Should().Be(rows[1]); }
            else { answer.Format().Should().Contain("cancelled"); }
        }
        else
        {
            await commands.Invoking(service => service.ExecuteAsync(follow, origin, () => true, f.Token))
                .Should().ThrowAsync<InvalidOperationException>();
        }
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("unique")]
    [InlineData("exact")]
    [InlineData("none")]
    [InlineData("too-many")]
    [InlineData("unadmitted")]
    [InlineData("missing")]
    [InlineData("invalid")]
    [InlineData("system")]
    [InlineData("late")]
    [InlineData("bound")]
    public async Task RemainingWorkResolutionIsExplicitBoundedAndNeverUsesFocusOrRecency(string mode)
    {
        using var f = new Fixture();
        var target = new SessionWorkspaceEntry(f.Session, new(f.Session.SessionId, new(1), new("Build")));
        var snapshot = RemainingSnapshot(target);
        f.ExactListReader = _ => target;
        f.OverviewReader = (_, _) => Task.FromResult(snapshot);
        var live = true;
        var rows = mode is "too-many" ? Enumerable.Range(0, 9).Select(_ =>
        {
            var id = new HostId<SessionIdentity>(Guid.NewGuid());
            return new SessionWorkspaceEntry(new(id, new(1), true), new(id, new(1), new("Build")));
        }).OrderBy(row => row.Authority.SessionId.Value.ToString("D"), StringComparer.Ordinal).ToImmutableArray() : [target];
        var bounded = Enumerable.Range(1, 16).Select(index =>
        {
            var id = new HostId<SessionIdentity>(Guid.Parse("00000000-0000-0000-0000-" + index.ToString("D12", System.Globalization.CultureInfo.InvariantCulture)));
            return new SessionWorkspaceEntry(new(id, new(1), true), new(id, new(1), new("Other")));
        }).ToArray();
        var boundedPage = 0;
        f.ListReader = (after, _) => mode is "none" ? new([], null) : mode is "bound"
            ? new([bounded[boundedPage]], bounded[boundedPage++].Authority.SessionId.Value) : new(rows, null);
        if (mode is "late") { f.AfterListRead = () => live = false; }
        if (mode is "unadmitted") { live = false; }
        await using var conversation = RemainingConversation(f);
        var commands = new SessionRemainingWorkCommandService(f.Service, conversation, f, f.QueueTime);
        var command = mode switch
        {
            "exact" => new SessionCommand(SessionCommandOperation.RemainingWork, f.Session.SessionId.Value),
            "missing" => new(SessionCommandOperation.RemainingWork),
            "invalid" => new(SessionCommandOperation.List),
            _ => new(SessionCommandOperation.RemainingWork) { RemainingTargetName = new("Build") },
        };
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        if (mode is "unique" or "exact")
        {
            (await commands.ExecuteAsync(command, RequestOrigin.LocalUi, () => live, f.Token)).Answer!.Session.Should().Be(target);
        }
        else
        {
            await commands.Invoking(service => service.ExecuteAsync(command,
                mode is "system" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi, () => live, f.Token))
                .Should().ThrowAsync<Exception>();
        }
        f.TaskWrites.Should().BeEmpty();
        f.ControlCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("head")]
    [InlineData("matches")]
    [InlineData("after-head")]
    [InlineData("after-read")]
    public async Task RemainingWorkNameResolutionRejectsChangingInventoryAndFinalSource(string change)
    {
        using var f = new Fixture();
        var target = new SessionWorkspaceEntry(f.Session, new(f.Session.SessionId, new(1), new("Build")));
        var snapshot = RemainingSnapshot(target);
        var reads = 0;
        var scans = 0;
        var finalCalls = 0;
        f.ExactListReader = _ => target;
        f.ListReader = (_, _) => ++scans == 2 && change is "matches" ? new([], null) : new([target], null);
        f.OverviewReader = (_, _) =>
        {
            reads++;
            return Task.FromResult(snapshot with
            {
                AuthorityRevision = change is "head" && reads == 2
                || change is "after-head" && reads >= 3 ? 2 : 1
            });
        };
        bool Live() => change is not "after-read" || reads < 4 || ++finalCalls < 3;
        await using var conversation = RemainingConversation(f);
        var commands = new SessionRemainingWorkCommandService(f.Service, conversation, f, f.QueueTime);
        using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
        await commands.Invoking(service => service.ExecuteAsync(new(SessionCommandOperation.RemainingWork)
        { RemainingTargetName = new("Build") }, RequestOrigin.LocalUi, Live, f.Token)).Should().ThrowAsync<InvalidOperationException>();
        f.TaskWrites.Should().BeEmpty();
    }

    private static SessionWorkSnapshot RemainingSnapshot(SessionWorkspaceEntry target,
        ImmutableArray<HostTaskObservation> tasks = default, int omitted = 0)
    {
        var request = RemainingRequest(target.Authority.SessionId);
        var now = new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
        return new(target, 1, now, new(target.Authority.SessionId, target.Authority.Generation, 0, []),
            0, 10, 1, [], 0, tasks.IsDefault ? [] : tasks, omitted,
            [new(new(request, new(Guid.NewGuid()), new(1)), target.Authority.Generation, QuestionStatus.Pending,
                QuestionKind.Text, null, now.AddMinutes(1))], 0);
    }

    private static HostRequest RemainingRequest(HostId<SessionIdentity> session) =>
        new(new(Guid.NewGuid()), session, new(Guid.NewGuid()), RequestOrigin.LocalUi);

    private sealed class RemainingClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
        internal Action? BeforeRead { get; set; }
        public override DateTimeOffset GetUtcNow() { BeforeRead?.Invoke(); return Now; }
    }
}