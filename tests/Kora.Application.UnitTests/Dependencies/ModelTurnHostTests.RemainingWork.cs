using System.Collections.Immutable;

using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.UnitTests.Dependencies;

public sealed partial class ModelTurnHostTests
{
    [Theory]
    [InlineData("current")]
    [InlineData("missing-record")]
    [InlineData("omitted-record")]
    [InlineData("generation")]
    [InlineData("revision")]
    [InlineData("running-generation")]
    [InlineData("cancel")]
    [InlineData("release")]
    [InlineData("expiry")]
    [InlineData("clock-back")]
    [InlineData("question-expiry")]
    [InlineData("privacy")]
    [InlineData("control")]
    [InlineData("disposed")]
    public async Task RemainingWorkJoinsOneOwnerObservationAndFencesDispatchReceiptAndLateChanges(string fault)
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var provider = new TaskCompletionSource<ModelProviderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Adapter.Pending = provider.Task;
        var target = new SessionWorkspaceEntry(f.Session, null);
        var first = await service.SubmitQueuedAsync(target, "private active", () => true, Token);
        var next = await service.SubmitQueuedAsync(target, "private pending", () => true, Token);
        f.Adapter.Calls.Should().Be(1);
        f.Time.ExpectedDeadline = TimeSpan.FromSeconds(10);
        try
        {
            if (fault is "missing-record" or "omitted-record")
            { f.RemainingProjection = value => value with { Tasks = [], OmittedTasks = fault is "omitted-record" ? 1 : 0 }; }
            if (fault is "generation" or "revision")
            {
                f.RemainingProjection = value => value with
                {
                    Tasks = [.. value.Tasks.Select(task =>
                    task.Task.Request == first.Receipt.Message.Request
                        ? task with { Generation = fault is "generation" ? new(2) : task.Generation,
                            Task = new(task.Task.Request, fault is "revision" ? new(1) : task.Task.Revision, task.Task.State) } : task)]
                };
            }
            if (fault is "running-generation") { f.Session = f.Session with { Generation = new(2) }; target = new(f.Session, null); }
            using var root = HostActivity.BeginRoot(f.Request, HostActivityLayer.Application, HostOperation.Request);
            if (fault is "missing-record" or "generation" or "revision" or "running-generation")
            {
                await service.Invoking(value => value.ReadRemainingWorkAsync(target, () => true, Token))
                    .Should().ThrowAsync<InvalidOperationException>().WithMessage("*changed*");
                return;
            }
            var answer = await service.ReadRemainingWorkAsync(target, () => true, Token);
            var body = answer.Format();
            body.Should().Contain("Actual host step: generating an answer").And.Contain("FIFO position 1")
                .And.NotContain("private").And.NotContain("Submitted request still owned");
            answer.Ordinary.Turns.Should().HaveCount(2);
            answer.Ordinary.Running!.Target.Request.Should().Be(first.Receipt.Message.Request);
            f.Adapter.Calls.Should().Be(1);
            first.Completion.IsCompleted.Should().BeFalse();
            if (fault is "cancel")
            {
                await service.CancelRunningConversationAsync(answer.Ordinary.Running.Target, () => true, Token);
                service.IsRemainingWorkCurrent(answer, () => true).Should().BeFalse();
                var cancelling = await service.ReadRemainingWorkAsync(target, () => true, Token);
                cancelling.Format().Should().Contain("cancellation accepted").And.Contain("Actual host step: unavailable");
            }
            if (fault is "release")
            {
                f.Adapter.Pending = null;
                f.Time.ExpectedDeadline = TimeSpan.FromSeconds(15);
                provider.TrySetResult(LateAnswer());
                await first.Completion;
                service.IsRemainingWorkCurrent(answer, () => true).Should().BeFalse();
            }
            if (fault is "expiry") { f.Time.Now = f.Time.Now.AddSeconds(11); }
            if (fault is "clock-back") { f.Time.Now = f.Time.Now.AddSeconds(-1); }
            if (fault is "question-expiry")
            {
                var question = new SessionWorkQuestion(new(f.Request, new(Guid.NewGuid()), new(1)),
                    target.Authority.Generation, Kora.Core.Interaction.QuestionStatus.Pending,
                    Kora.Core.Interaction.QuestionKind.Text, null, f.Time.Now.AddSeconds(1));
                answer = answer with { Questions = [question] };
                f.Time.Now = f.Time.Now.AddSeconds(2);
            }
            if (fault is "privacy") { f.CanControl = false; }
            if (fault is "control") { f.ControlRevision++; }
            if (fault is "disposed") { var disposal = service.DisposeAsync(); provider.TrySetResult(LateAnswer()); await disposal; }
            if (fault is "expiry" or "clock-back" or "question-expiry" or "privacy" or "control" or "disposed")
            { service.IsRemainingWorkCurrent(answer, () => true).Should().BeFalse(); }
            if (fault is "current") { await service.ValidateRemainingWorkAsync(answer, () => true, Token); }
        }
        finally
        {
            f.Time.ExpectedDeadline = TimeSpan.FromSeconds(15);
            var disposal = service.DisposeAsync();
            provider.TrySetResult(LateAnswer());
            await disposal;
            await first.Completion;
            await next.Completion;
        }
    }

    [Fact]
    public async Task RemainingWorkAcceptingOriginalIsExplicitlyIncompleteAndCannotSurviveRetentionTransition()
    {
        using var f = new Fixture { TrackPendingRecords = true };
        var host = ConfigureCancellation(f);
        await using var service = f.ConversationService(host, execution: host);
        var target = new SessionWorkspaceEntry(f.Session, null);
        Task<Kora.Application.Hosting.SessionRemainingWorkAnswer>? observation = null;
        f.AfterRetain = () =>
        {
            f.Time.ExpectedDeadline = TimeSpan.FromSeconds(10);
            observation = service.ReadRemainingWorkAsync(target, () => true, Token);
            f.Time.ExpectedDeadline = TimeSpan.FromSeconds(15);
        };
        await service.SubmitAsync(target, "original", () => true, Token);
        var answer = await observation!;
        answer.Format().Should().Contain("Accepting originals not yet retained: 1");
        service.IsRemainingWorkCurrent(answer, () => true).Should().BeFalse();
    }

    private sealed partial class Fixture : ISessionWorkStore
    {
        internal Func<SessionWorkSnapshot, SessionWorkSnapshot>? RemainingProjection { get; set; }
        public ValueTask<SessionWorkSnapshot> ReadWorkAsync(HostId<SessionIdentity> session, long admissionRevision,
            SessionQueueLimits limits, CancellationToken token)
        {
            var tasks = TrackPendingRecords ? PendingRecords.Values.Select(item => new HostTaskObservation(item.Task,
                item.Message.Generation, "ordinary", true, null)).ToImmutableArray()
                : Task is { } task ? [task] : ImmutableArray<HostTaskObservation>.Empty;
            tasks = [.. tasks.OrderBy(task => task.Task.Request.TaskId.Value)];
            var snapshot = new SessionWorkSnapshot(new(Session, Metadata), 1, Time.Now,
                new(session, Session.Generation, 0, []), 0, 10, 1, [], 0, tasks, 0, [], 0);
            return ValueTask.FromResult(RemainingProjection?.Invoke(snapshot) ?? snapshot);
        }
    }
}