using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SessionHistorySearchTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("%%%")]
    [InlineData("\ud800")]
    public void Invalid_queries_fail_explicitly(string query)
    {
        var act = () => new SessionHistorySearch(query);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Query_boundaries_reuse_exact_Unicode_lexical_policy()
    {
        _ = new SessionHistorySearch(new string('a', 64));
        _ = new SessionHistorySearch(new string(' ', 192) + new string('a', 64));
        _ = new SessionHistorySearch(new string('界', 64) + new string(' ', 192));
        _ = new SessionHistorySearch(string.Join(' ', Enumerable.Range(0, 32)));
        _ = new SessionHistorySearch(new string('界', 64) + new string(' ', 128) + new string('界', 64));
        foreach (var query in new[] { new string('a', 65), new string(' ', 193) + new string('a', 64),
            string.Join(' ', Enumerable.Repeat(new string('界', 64), 3)), string.Join(' ', Enumerable.Range(0, 33)),
            new string('界', 64) + new string(' ', 63) + "é" + new string(' ', 64) + new string('界', 64) })
        {
            var act = () => new SessionHistorySearch(query);
            act.Should().Throw<ArgumentException>();
        }
        new SessionHistorySearch("café").Digest.Should().Be(new SessionHistorySearch("CAFE\u0301 café").Digest);
    }

    [Theory]
    [InlineData("question", true)]
    [InlineData("café", true)]
    [InlineData("cafe\u0301", true)]
    [InlineData("answer", true)]
    [InlineData("option", true)]
    [InlineData("choice", true)]
    [InlineData("Succeeded", true)]
    [InlineData("Answered", true)]
    [InlineData("Pending", true)]
    [InlineData("ques", false)]
    [InlineData("nonexistent", false)]
    public void Search_uses_only_admitted_fields_with_literal_OR_whole_words(string query, bool expected)
    {
        var record = Record() with { Question = "question café", Answer = "answer", Options = [new("id", "option")],
            Choices = ["choice"], TaskState = HostTaskState.Succeeded, Decision = HostInteractionOutcome.Answered,
            QuestionStatus = QuestionStatus.Pending };
        new SessionHistorySearch(query).Matches(record, TestContext.Current.CancellationToken).Should().Be(expected);
        new SessionHistorySearch("unmatched café").Matches(record, TestContext.Current.CancellationToken).Should().BeTrue();
        new SessionHistorySearch("a").Matches(record with { Question = new('a', 65), Answer = null }, TestContext.Current.CancellationToken).Should().BeFalse();
    }

    [Theory]
    [InlineData(SessionHistoryAvailability.Gap)]
    [InlineData(SessionHistoryAvailability.Redacted)]
    [InlineData(SessionHistoryAvailability.Unavailable)]
    public void Unavailable_fields_never_match(SessionHistoryAvailability availability) =>
        new SessionHistorySearch("question").Matches(Record() with { Availability = availability, Question = "question" },
            TestContext.Current.CancellationToken).Should().BeFalse();

    [Fact]
    public void Cancellation_and_result_size_are_explicit()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var act = () => new SessionHistorySearch("question").Matches(Record(), cancelled.Token);
        act.Should().Throw<OperationCanceledException>();
        var result = new SessionCommandResult("observed", "receipt");
        SessionCommandResult.GetSerializedSize(result).Should().Be(SessionCommandResult.Serialize(result).Length);
        new SessionHistorySearch("absent").Matches(Record(), TestContext.Current.CancellationToken).Should().BeFalse();
    }

    private static SessionHistoryEvent Record() => new(Guid.NewGuid(), new(Guid.NewGuid()), 1, new(1),
        SessionHistoryKind.Question, SessionHistoryAvailability.Available, null, null, null, 1, new('a', 64),
        null, false, null, [], null, [], null, null, null, null);

    [Theory]
    [InlineData("", true)]
    [InlineData("limit 1", true)]
    [InlineData("limit 50", true)]
    [InlineData("limit 0", false)]
    [InlineData("limit 51", false)]
    [InlineData("limit", false)]
    [InlineData("limit x", false)]
    [InlineData("extra", false)]
    [InlineData("after", false)]
    [InlineData("after 1:1:1", false)]
    [InlineData("after 0:1:1:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", false)]
    [InlineData("after 1:0:1:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", false)]
    [InlineData("after 1:1:0:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", false)]
    [InlineData("after 1:1:2:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", false)]
    [InlineData("after 1:1:1:bad", false)]
    [InlineData("after 1:1:1:GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG", false)]
    [InlineData("after 1:1:1:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA limit 25", true)]
    public void Exact_original_input_grammar_is_bounded(string arguments, bool accepted)
    {
        var input = $"Nova, session search {Guid.NewGuid():D} {arguments} \"café\"";
        var command = SessionCommand.Parse(input, "Nova")!;
        command.Operation.Should().Be(accepted ? SessionCommandOperation.HistorySearch : SessionCommandOperation.Invalid);
    }

    [Theory]
    [InlineData("session search")]
    [InlineData("session search name \"word\"")]
    [InlineData("session search 00000000-0000-0000-0000-000000000001")]
    [InlineData("session search 00000000-0000-0000-0000-000000000001 \"%%%\"")]
    [InlineData("session search 00000000-0000-0000-0000-000000000001 \"\"")]
    [InlineData("session search 00000000-0000-0000-0000-000000000001\"word\"")]
    [InlineData("session search 00000000-0000-0000-0000-000000000001 \"word")]
    [InlineData("session search 00000000-0000-0000-0000-000000000001 \"word\" trailing")]
    [InlineData("\"")]
    public void Missing_invalid_or_untrusted_search_syntax_never_becomes_a_query(string input)
    {
        var result = SessionCommand.Parse(input, "Nova");
        if (result is not null) { result.Operation.Should().Be(SessionCommandOperation.Invalid); }
    }
}
