using System.Collections.Immutable;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed class ClarificationExchangeTests
{
    [Fact]
    public void ExactRequestAndTaskMatchDoesNotJoinIdenticalProseOrUnattributedGaps()
    {
        var message = Message();
        var exact = Event(message, 1);
        var exchange = new ClarificationExchange(message, static () => true);
        exchange.Present(Page(message, [
            exact,
            exact with { Id = Guid.NewGuid(), Sequence = 2, RequestId = Guid.NewGuid() },
            exact with { Id = Guid.NewGuid(), Sequence = 3, TaskId = Guid.NewGuid() },
            exact with { Id = Guid.NewGuid(), Sequence = 4, RequestId = null, TaskId = null,
                Kind = SessionHistoryKind.Gap, Availability = SessionHistoryAvailability.Gap },
        ]), null);
        exchange.Records.Should().ContainSingle().Which.Should().Contain("same question");
        exchange.Status.Should().Contain("1 exact").And.Contain("gap").And.Contain("cannot be attributed");
    }

    [Theory]
    [InlineData(SessionHistoryAvailability.MetadataOnly)]
    [InlineData(SessionHistoryAvailability.Gap)]
    [InlineData(SessionHistoryAvailability.Redacted)]
    [InlineData(SessionHistoryAvailability.Unavailable)]
    public void UnavailableRecordsNeverExposeEvenSuppliedQuestionOptionsOrAnswer(SessionHistoryAvailability availability)
    {
        var message = Message();
        var exchange = new ClarificationExchange(message, static () => true);
        exchange.Present(Page(message, [Event(message, 1) with { Availability = availability }]), null);
        exchange.Records.Single().Should().Contain(availability.ToString()).And.NotContain("same question")
            .And.NotContain("secret answer").And.NotContain("First choice");
    }

    [Theory]
    [InlineData(QuestionStatus.Pending)]
    [InlineData(QuestionStatus.Cancelled)]
    [InlineData(QuestionStatus.Expired)]
    public void NonAnsweredStatesNeverPromoteDraftOrChoicesToAcceptedAnswer(QuestionStatus status)
    {
        var message = Message();
        var exchange = new ClarificationExchange(message, static () => true);
        exchange.Present(Page(message, [Event(message, 1) with { QuestionStatus = status }]), null);
        exchange.Records.Single().Should().Contain(status.ToString()).And.Contain("No accepted answer")
            .And.NotContain("secret answer").And.NotContain("Accepted choice");
    }

    [Fact]
    public void AcceptedTextChoicesOriginalChannelAndObservedOutcomesRemainInertAndOrdered()
    {
        var message = Message();
        var exchange = new ClarificationExchange(message, static () => true);
        exchange.Present(Page(message, [
            Event(message, 1),
            Event(message, 2) with { Kind = SessionHistoryKind.Task, Availability = SessionHistoryAvailability.MetadataOnly,
                TaskState = HostTaskState.Cancelled },
            Event(message, 3) with { Kind = SessionHistoryKind.Decision, Availability = SessionHistoryAvailability.MetadataOnly,
                Decision = HostInteractionOutcome.Cancelled, Baseline = true },
        ]), null);
        exchange.Records[0].Should().Contain("Accepted answer: secret answer").And.Contain("Accepted choice: first")
            .And.Contain("Accepted choice: second").And.Contain("ActivatedVoice").And.Contain("Option first: First choice");
        exchange.Records[1].Should().Contain("Observed task outcome: Cancelled");
        exchange.Records[2].Should().Contain("Observed host decision: Cancelled");
        exchange.Status.Should().Contain("Baseline").And.Contain("No replay").And.Contain("not a guarantee");
        exchange.Withdraw();
        exchange.Records.Should().BeEmpty();
        exchange.Page.Should().BeNull();
    }

    [Fact]
    public void FiftyRecordPageAndZeroMatchContinuationKeepFixedCeilingAndDoNotAccumulateBodies()
    {
        var message = Message();
        var exchange = new ClarificationExchange(message, static () => true);
        var rows = Enumerable.Range(1, 50).Select(index => Event(message, index) with { TaskId = Guid.NewGuid() }).ToImmutableArray();
        var cursor = new SessionHistoryCursor(message.Request.SessionId, new(1), 51, 50);
        exchange.Present(new(message.Request.SessionId, new(1), false, 51, rows, cursor), null);
        exchange.Records.Should().BeEmpty();
        exchange.Status.Should().Contain("Partial exchange").And.Contain("not proof");
        exchange.Page!.Next.Should().Be(cursor);
        exchange.Present(new(message.Request.SessionId, new(1), false, 51, [Event(message, 51)], null), cursor);
        exchange.Records.Should().ContainSingle();
        exchange.Status.Should().Contain("End of this fixed").And.Contain("earlier pages are not held");
        exchange.Invoking(owner => owner.Present(new(message.Request.SessionId, new(1), false, 52,
            [Event(message, 52)], null), cursor)).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("session")]
    [InlineData("generation")]
    [InlineData("sequence")]
    [InlineData("count")]
    [InlineData("bytes")]
    [InlineData("disposed")]
    [InlineData("cursor")]
    public void InvalidOrOverBudgetPageFailsClosed(string boundary)
    {
        var message = Message();
        var row = Event(message, 1);
        var page = Page(message, [row]);
        page = boundary switch
        {
            "session" => page with { SessionId = new(Guid.NewGuid()) },
            "generation" => page with { Records = [row with { Generation = new(2) }] },
            "sequence" => page with { Records = [row with { Sequence = 2 }] },
            "count" => page with { Records = Enumerable.Range(1, 51).Select(index => row with { Sequence = index }).ToImmutableArray(), Snapshot = 51 },
            "bytes" => page with { Records = [row with { Question = new string('x', 65536) }] },
            "disposed" => page with { Disposed = true },
            _ => page with { Next = new(message.Request.SessionId, new(1), 2, 1) },
        };
        var exchange = new ClarificationExchange(message, static () => true);
        exchange.Invoking(owner => owner.Present(page, null)).Should().Throw<InvalidDataException>();
        exchange.Records.Should().BeEmpty();
        exchange.Page.Should().BeNull();
    }

    [Fact]
    public void MissingAcceptedPayloadAndChannelAreLabelledWithoutInferringAnAnswer()
    {
        var message = Message();
        var exchange = new ClarificationExchange(message, static () => true);
        exchange.Present(Page(message, [Event(message, 1) with { Answer = null, Choices = [], AnswerChannel = null }]), null);
        exchange.Records.Single().Should().Contain("No accepted text or choices retained")
            .And.Contain("Original answer channel: not retained").And.NotContain("secret answer");
    }

    [Fact]
    public void ExactUtf8ByteLimitIsAcceptedAndOneAdditionalByteIsRefused()
    {
        var message = Message();
        var empty = Event(message, 1) with { Question = string.Empty };
        var overhead = JsonSerializer.SerializeToUtf8Bytes(empty).Length;
        var row = empty with { Question = new string('x', 65536 - overhead) };
        JsonSerializer.SerializeToUtf8Bytes(row).Length.Should().Be(65536);
        var exchange = new ClarificationExchange(message, static () => true);
        exchange.Present(Page(message, [row]), null);
        exchange.Records.Should().ContainSingle();
        var next = new ClarificationExchange(message, static () => true);
        next.Invoking(owner => owner.Present(Page(message, [row with { Question = row.Question + "x" }]), null))
            .Should().Throw<InvalidDataException>();
        next.Records.Should().BeEmpty();
    }

    internal static ConversationMessage Message() =>
        new(HostRequest.Create(RequestOrigin.LocalUi), new(1), 1, 1, DateTimeOffset.UtcNow, "original turn", null, null);

    internal static SessionHistoryEvent Event(ConversationMessage message, long sequence) =>
        new(Guid.NewGuid(), message.Request.SessionId, sequence, new(1), SessionHistoryKind.Answer,
            SessionHistoryAvailability.Available, message.Request.RequestId.Value, message.Request.TaskId.Value,
            Guid.NewGuid(), 2, new string('a', 64), null, false, "same question",
            [new("first", "First choice"), new("second", "Second choice")], "secret answer", ["first", "second"],
            null, null, QuestionStatus.Answered, RequestOrigin.ActivatedVoice);

    private static SessionHistoryPage Page(ConversationMessage message, ImmutableArray<SessionHistoryEvent> rows) =>
        new(message.Request.SessionId, new(1), false, rows[^1].Sequence, rows, null);
}
