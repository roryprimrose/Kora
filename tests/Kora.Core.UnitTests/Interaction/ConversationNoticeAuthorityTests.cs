using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Interaction.ConversationNotices;

namespace Kora.Core.UnitTests.Interaction;

public sealed class ConversationNoticeAuthorityTests
{
    private static readonly DateTimeOffset Now = LocalEventTests.Now;
    private static readonly HostRequest Request = HostRequest.Create(RequestOrigin.LocalUi);

    private static ConversationNoticeAuthority Notice() => new(Request, new(1), 3, new(Guid.NewGuid()),
        Now, ModelTurnOutcome.Succeeded, ModelTurnReason.ProviderOutcome, null, null);
    private static HostTaskRecord Task(HostTaskState state = HostTaskState.Succeeded, long revision = 3) =>
        new(Request, new(revision), state);

    [Theory]
    [InlineData(ModelTurnOutcome.Succeeded, HostTaskState.Succeeded)]
    [InlineData(ModelTurnOutcome.Unavailable, HostTaskState.Unavailable)]
    [InlineData(ModelTurnOutcome.Cancelled, HostTaskState.Unknown)]
    [InlineData(ModelTurnOutcome.TimedOut, HostTaskState.Unknown)]
    [InlineData(ModelTurnOutcome.Unknown, HostTaskState.Unknown)]
    [InlineData(ModelTurnOutcome.AuthenticationRequired, HostTaskState.Denied)]
    [InlineData(ModelTurnOutcome.QuotaExceeded, HostTaskState.Denied)]
    [InlineData(ModelTurnOutcome.Denied, HostTaskState.Denied)]
    [InlineData(ModelTurnOutcome.DeniedEgress, HostTaskState.Denied)]
    public void TerminalRequiresExactDurableOutcome(ModelTurnOutcome outcome, HostTaskState state)
    {
        var notice = Notice() with { Outcome = outcome };
        notice.Validate(Task(state));
        notice.Invoking(item => item.Validate(Task(HostTaskState.DispatchRecorded))).Should().Throw<InvalidDataException>();
        notice.Invoking(item => item.Validate(Task(state, 4))).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("request")]
    [InlineData("invocation")]
    [InlineData("origin")]
    [InlineData("generation")]
    [InlineData("revision")]
    [InlineData("minimum")]
    [InlineData("offset")]
    [InlineData("overflow")]
    [InlineData("missing-outcome")]
    [InlineData("missing-reason")]
    [InlineData("outcome")]
    [InlineData("reason")]
    [InlineData("recovered")]
    [InlineData("question-only")]
    [InlineData("expiry-only")]
    [InlineData("pending")]
    [InlineData("early-terminal")]
    [InlineData("turn")]
    public void InvalidSourceCannotBecomeNoticeAuthority(string fault)
    {
        var notice = Notice();
        notice = fault switch
        {
            "request" => notice with { Request = HostRequest.Create(RequestOrigin.LocalUi) },
            "invocation" => notice with { Request = new(Request.RequestId, Request.SessionId, Request.TaskId,
                Request.Origin, new(Guid.NewGuid())) },
            "origin" => notice with { Request = new(Request.RequestId, Request.SessionId, Request.TaskId, RequestOrigin.HostSystem) },
            "generation" => notice with { Generation = default },
            "revision" => notice with { MessageRevision = 4 },
            "minimum" => notice with { MessageRevision = 1 },
            "offset" => notice with { SubmittedAt = Now.ToOffset(TimeSpan.FromHours(1)) },
            "overflow" => notice with { SubmittedAt = DateTimeOffset.MaxValue },
            "missing-outcome" => notice with { Outcome = null },
            "missing-reason" => notice with { Reason = null },
            "outcome" => notice with { Outcome = (ModelTurnOutcome)99 },
            "reason" => notice with { Reason = (ModelTurnReason)99 },
            "recovered" => notice with { Reason = ModelTurnReason.HostInterrupted },
            "question-only" => notice with { Question = new(Request, new(Guid.NewGuid()), new(1)) },
            "expiry-only" => notice with { QuestionExpiresAt = Now.AddSeconds(15) },
            "pending" => notice with { Outcome = null, Reason = null },
            "early-terminal" => notice with { MessageRevision = 2 },
            _ => notice with { Turn = default },
        };
        var task = fault is "invocation" or "origin" ? new HostTaskRecord(notice.Request, new(3), HostTaskState.Succeeded)
            : fault is "minimum" or "early-terminal" ? Task(revision: notice.MessageRevision) : Task();
        notice.Invoking(item => item.Validate(task)).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("terminal")]
    [InlineData("revision")]
    [InlineData("state")]
    [InlineData("request")]
    [InlineData("offset")]
    [InlineData("early")]
    [InlineData("late")]
    public void ClarificationRequiresExactOriginalPendingTask(string fault)
    {
        var notice = Notice() with { MessageRevision = 2, Outcome = null, Reason = null,
            Question = new(Request, new(Guid.NewGuid()), new(1)), QuestionExpiresAt = Now.AddSeconds(15) };
        notice = fault switch
        {
            "terminal" => notice with { Outcome = ModelTurnOutcome.Succeeded, Reason = ModelTurnReason.ProviderOutcome },
            "revision" => notice with { MessageRevision = 3 },
            "request" => notice with { Question = new(HostRequest.Create(RequestOrigin.LocalUi), notice.Question!.QuestionId, new(1)) },
            "offset" => notice with { QuestionExpiresAt = Now.AddSeconds(15).ToOffset(TimeSpan.FromHours(1)) },
            "early" => notice with { QuestionExpiresAt = Now },
            "late" => notice with { QuestionExpiresAt = Now.AddHours(1) },
            _ => notice,
        };
        var task = Task(fault is "state" ? HostTaskState.Succeeded : HostTaskState.DispatchRecorded, notice.MessageRevision);
        if (fault is "valid") { notice.Validate(task); }
        else { notice.Invoking(item => item.Validate(task)).Should().Throw<InvalidDataException>(); }
    }

    [Theory]
    [InlineData(LocalEventType.Completed)]
    [InlineData(LocalEventType.Failed)]
    [InlineData(LocalEventType.Unknown)]
    [InlineData(LocalEventType.UserAttention)]
    public void AdditiveSourcesKeepStableIdsClosedTypesAndSuppressionRoundtrip(LocalEventType type)
    {
        var source = type == LocalEventType.UserAttention ? LocalEventSource.ConversationQuestion : LocalEventSource.ConversationTask;
        var item = LocalEventTests.Event(type) with { Source = source, QueueReason = null,
            Id = LocalEvent.Identity(source, Request.SessionId.Value, Request.TaskId.Value),
            SessionId = Request.SessionId, TaskId = Request.TaskId, SubjectId = Request.TaskId.Value };
        item.Validate();
        item.Summary.Should().Contain("conversation").And.NotContain("local-version");
        item.Summary.Length.Should().BeLessThanOrEqualTo(256);
        item.SameSource(item with { ObservedAt = Now.AddMinutes(1), Revision = 2 }).Should().BeTrue();
        (item with { Type = LocalEventType.Queued }).Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
        (item with { QueueReason = SessionQueueEligibility.Ready }).Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
        var state = LocalEventBrokerState.Empty(Now) with { Receipts =
            [new(item, LocalEventDisposition.Presented, null, Now)] };
        LocalEventBrokerState.Deserialize(LocalEventBrokerState.Serialize(state)).Receipts.Single().Event.Should().Be(item);
    }
}
