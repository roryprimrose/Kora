using AwesomeAssertions;

using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SessionHistorySearchTests
{
    [Fact]
    public void Exact_metadata_is_AND_composed_with_unchanged_lexical_OR_and_missing_fields_never_match()
    {
        var token = TestContext.Current.CancellationToken;
        var record = Record() with { Question = "café", TaskState = HostTaskState.Succeeded,
            Decision = HostInteractionOutcome.Answered, QuestionStatus = QuestionStatus.Answered,
            AnswerChannel = RequestOrigin.LocalUi };
        var all = new SessionHistoryMetadataFilter(SessionHistoryKind.Question, HostTaskState.Succeeded,
            HostInteractionOutcome.Answered, QuestionStatus.Answered, RequestOrigin.LocalUi);
        var search = new SessionHistorySearch("absent CAFE\u0301", all);
        search.Matches(record, token).Should().BeTrue();
        new SessionHistorySearch("absent", all).Matches(record, token).Should().BeFalse();
        search.Digest.Should().Be(new SessionHistorySearch("CAFÉ absent café", all).Digest);
        var filters = new[] { default(SessionHistoryMetadataFilter),
            new(Kind: SessionHistoryKind.Answer), new(TaskState: HostTaskState.Failed),
            new(Decision: HostInteractionOutcome.Approved), new(QuestionStatus: QuestionStatus.Pending),
            new(AnswerChannel: RequestOrigin.ActivatedVoice) };
        foreach (var filter in filters)
        {
            var exact = new SessionHistorySearch("café", filter);
            exact.Digest.Should().NotBe(search.Digest);
            exact.Matches(record, token).Should().Be(filter == default);
        }
        foreach (var filter in new[] { new SessionHistoryMetadataFilter(TaskState: HostTaskState.Succeeded),
            new(Decision: HostInteractionOutcome.Answered), new(QuestionStatus: QuestionStatus.Answered),
            new(AnswerChannel: RequestOrigin.LocalUi) })
        {
            new SessionHistorySearch("café", filter).Matches(Record() with { Question = "café" }, token).Should().BeFalse();
        }
        foreach (var availability in Enum.GetValues<SessionHistoryAvailability>())
        {
            search.Matches(record with { Availability = availability }, token).Should()
                .Be(availability is SessionHistoryAvailability.Available or SessionHistoryAvailability.MetadataOnly);
        }
        new SessionHistorySearch("café", new(AnswerChannel: RequestOrigin.HostSystem))
            .Matches(record, token).Should().BeFalse();
        // ConversationSearch consumes this shared default digest, not the receipt metadata envelope.
        new SessionHistorySearch("CAFÉ café").Digest.Should().Be(Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("CAFÉ"))));
    }

    [Fact]
    public void Every_defined_predicate_has_a_distinct_canonical_digest_and_undefined_values_fail_closed()
    {
        var filters = Enum.GetValues<SessionHistoryKind>().Select(value => new SessionHistoryMetadataFilter(Kind: value))
            .Concat(Enum.GetValues<HostTaskState>().Select(value => new SessionHistoryMetadataFilter(TaskState: value)))
            .Concat(Enum.GetValues<HostInteractionOutcome>().Select(value => new SessionHistoryMetadataFilter(Decision: value)))
            .Concat(Enum.GetValues<QuestionStatus>().Select(value => new SessionHistoryMetadataFilter(QuestionStatus: value)))
            .Concat(Enum.GetValues<RequestOrigin>().Select(value => new SessionHistoryMetadataFilter(AnswerChannel: value)))
            .Append(default).ToArray();
        filters.Select(filter => new SessionHistorySearch("word", filter).Digest).Should().OnlyHaveUniqueItems();
        foreach (var filter in new[] { new SessionHistoryMetadataFilter(Kind: (SessionHistoryKind)(-1)),
            new(TaskState: (HostTaskState)999), new(Decision: (HostInteractionOutcome)999),
            new(QuestionStatus: (QuestionStatus)999), new(AnswerChannel: (RequestOrigin)999) })
        {
            var act = () => new SessionHistorySearch("word", filter);
            act.Should().Throw<ArgumentOutOfRangeException>();
        }
    }

    [Fact]
    public void Receipt_shape_validation_rejects_corruption_before_any_matching()
    {
        var valid = Record() with { RequestId = Guid.NewGuid(), TaskId = Guid.NewGuid(), SourceId = Guid.NewGuid(),
            SourceRevision = 0, AuditSequence = 1, TaskState = HostTaskState.Succeeded,
            Decision = HostInteractionOutcome.Answered, QuestionStatus = QuestionStatus.Answered, AnswerChannel = RequestOrigin.ActivatedVoice };
        valid.Validate();
        Record().Validate();
        (valid with { AnswerChannel = RequestOrigin.LocalUi }).Validate();
        foreach (var invalid in new[] { valid with { Id = Guid.Empty }, valid with { Sequence = 0 },
            valid with { SourceRevision = -1 }, valid with { AuditSequence = 0 }, valid with { Generation = default },
            valid with { Kind = (SessionHistoryKind)999 }, valid with { Availability = (SessionHistoryAvailability)999 },
            valid with { TaskState = (HostTaskState)999 }, valid with { Decision = (HostInteractionOutcome)999 },
            valid with { QuestionStatus = (QuestionStatus)999 }, valid with { AnswerChannel = RequestOrigin.HostSystem },
            valid with { Options = default }, valid with { Choices = default },
            valid with { RequestId = Guid.Empty }, valid with { TaskId = Guid.Empty }, valid with { SourceId = Guid.Empty },
            valid with { ProvenanceDigest = null! }, valid with { ProvenanceDigest = "bad" },
            valid with { ProvenanceDigest = new('g', 64) } })
        {
            var act = invalid.Validate;
            act.Should().Throw<InvalidDataException>();
        }
    }

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
