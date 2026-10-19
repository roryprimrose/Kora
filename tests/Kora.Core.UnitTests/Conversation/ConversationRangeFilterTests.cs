using System.Globalization;

using AwesomeAssertions;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Conversation;

public sealed class ConversationRangeFilterTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(long.MaxValue, 1)]
    [InlineData(long.MaxValue, 2)]
    public void ObservedProvenanceUsesInclusiveDomainBoundsForBothRoles(long generation, long revision)
    {
        var message = Message() with { Generation = new(generation), Revision = revision,
            Outcome = revision == 1 ? null : ModelTurnOutcome.Unavailable,
            Reason = revision == 1 ? null : ModelTurnReason.UnregisteredModel };
        message.Validate();
        var range = new ConversationRangeFilter(MinimumGeneration: generation, MaximumGeneration: generation,
            MinimumRevision: revision, MaximumRevision: revision);
        var token = TestContext.Current.CancellationToken;
        new ConversationSearch("marker", ConversationSearchRole.USER, range: range).Matches(message, token).Should().BeTrue();
        new ConversationSearch(message.ExpectedTaskState.ToString(), ConversationSearchRole.HOST, range: range)
            .Matches(message, token).Should().BeTrue();
        foreach (var excluded in new[] { range with { MinimumGeneration = long.MaxValue, MaximumGeneration = null },
            range with { MinimumGeneration = null, MaximumGeneration = 1 },
            range with { MinimumRevision = 2, MaximumRevision = null },
            range with { MinimumRevision = null, MaximumRevision = 1 } })
        {
            excluded.Validate();
            new ConversationSearch("marker", range: excluded).Matches(message, token).Should().Be(
                (excluded.MinimumGeneration is not { } minGen || generation >= minGen)
                && (excluded.MaximumGeneration is not { } maxGen || generation <= maxGen)
                && (excluded.MinimumRevision is not { } minRev || revision >= minRev)
                && (excluded.MaximumRevision is not { } maxRev || revision <= maxRev));
        }
    }

    [Fact]
    public void ProvenanceBoundsRejectInvalidDomainValuesAndBindEveryEndpoint()
    {
        foreach (var invalid in new[] {
            new ConversationRangeFilter(MinimumGeneration: 0), new ConversationRangeFilter(MaximumGeneration: -1),
            new ConversationRangeFilter(MinimumGeneration: 2, MaximumGeneration: 1),
            new ConversationRangeFilter(MinimumRevision: 0), new ConversationRangeFilter(MinimumRevision: 4),
            new ConversationRangeFilter(MaximumRevision: 0), new ConversationRangeFilter(MaximumRevision: 4),
            new ConversationRangeFilter(MinimumRevision: 2, MaximumRevision: 1) })
        { new Action(() => _ = new ConversationSearch("marker", range: invalid)).Should().Throw<ArgumentException>(); }
        var range = ConversationRangeFilter.Parse("", "", "", "", "01", long.MaxValue.ToString(CultureInfo.InvariantCulture), "01", "02");
        range.Should().Be(new ConversationRangeFilter(MinimumGeneration: 1, MaximumGeneration: long.MaxValue,
            MinimumRevision: 1, MaximumRevision: 2));
        var search = new ConversationSearch("marker", range: range);
        foreach (var changed in new[] { range with { MinimumGeneration = 2 }, range with { MaximumGeneration = 2 },
            range with { MinimumRevision = 2 }, range with { MaximumRevision = 1 }, default })
        { new ConversationSearch("marker", range: changed).Digest.Should().NotBe(search.Digest); }
        new ConversationSearch("MARKER marker", range: range).Digest.Should().Be(search.Digest);
        new Action(() => new ConversationSearch("absent", range: new(MinimumGeneration: 2)).Matches(
            Message() with { Generation = default }, TestContext.Current.CancellationToken)).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(" ")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("+1")]
    [InlineData("-1")]
    [InlineData("1.0")]
    [InlineData("1e1")]
    [InlineData("9223372036854775808")]
    [InlineData("unknown")]
    [InlineData("\uff10\uff11")]
    public void EveryProvenanceInputUsesStrictUnsignedDecimal(string invalid)
    {
        new Action(() => ConversationRangeFilter.Parse("", "", "", "", invalid)).Should().Throw<InvalidDataException>();
        new Action(() => ConversationRangeFilter.Parse("", "", "", "", "", invalid)).Should().Throw<InvalidDataException>();
        new Action(() => ConversationRangeFilter.Parse("", "", "", "", "", "", invalid)).Should().Throw<InvalidDataException>();
        new Action(() => ConversationRangeFilter.Parse("", "", "", "", "", "", "", invalid)).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void InclusiveBoundsComposeWithoutChangingOriginalFieldsOrRole()
    {
        var message = Message();
        var token = TestContext.Current.CancellationToken;
        var exact = new ConversationRangeFilter(2, 2, message.SubmittedAt, message.SubmittedAt);
        foreach (var range in new[] { default, exact, new ConversationRangeFilter(1, ConversationText.MaximumSessionMessages),
            new ConversationRangeFilter(2), new ConversationRangeFilter(MaximumSequence: 2),
            new ConversationRangeFilter(SubmittedFrom: DateTimeOffset.MinValue),
            new ConversationRangeFilter(SubmittedThrough: DateTimeOffset.MaxValue) })
        {
            range.Validate();
            range.Matches(message).Should().BeTrue();
            new ConversationSearch("marker", ConversationSearchRole.USER, range: range).Matches(message, token).Should().BeTrue();
        }
        foreach (var range in new[] { new ConversationRangeFilter(3), new ConversationRangeFilter(MaximumSequence: 1),
            new ConversationRangeFilter(SubmittedFrom: message.SubmittedAt.AddTicks(1)),
            new ConversationRangeFilter(SubmittedThrough: message.SubmittedAt.AddTicks(-1)) })
        {
            range.Validate();
            new ConversationSearch("marker", range: range).Matches(message, token).Should().BeFalse();
        }
        var terminal = new ConversationTerminalFilter(message.Outcome, message.Reason, message.ExpectedTaskState);
        new ConversationSearch("marker", terminal: terminal, range: exact).Matches(message, token).Should().BeTrue();
        new ConversationSearch("marker", ConversationSearchRole.USER, terminal, exact).Matches(message, token).Should().BeFalse();
        new ConversationSearch("marker", ConversationSearchRole.HOST, range: exact).Matches(message, token).Should().BeFalse();
        new ConversationSearch("Unavailable", ConversationSearchRole.HOST, terminal, exact).Matches(message, token).Should().BeTrue();
        new ConversationSearch("absent", terminal: terminal, range: exact).Matches(message, token).Should().BeFalse();
        new ConversationSearch("marker", terminal: new(ModelTurnOutcome.Denied), range: exact).Matches(message, token).Should().BeFalse();
        message.Should().Be(Message());
    }

    [Fact]
    public void InvalidRangesNeverBecomeUnrestricted()
    {
        foreach (var range in new[] { new ConversationRangeFilter(0), new ConversationRangeFilter(-1),
            new ConversationRangeFilter(ConversationText.MaximumSessionMessages + 1), new ConversationRangeFilter(MaximumSequence: 0),
            new ConversationRangeFilter(MaximumSequence: ConversationText.MaximumSessionMessages + 1),
            new ConversationRangeFilter(2, 1), new ConversationRangeFilter(SubmittedFrom: DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(1))),
            new ConversationRangeFilter(SubmittedThrough: DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(-1))),
            new ConversationRangeFilter(SubmittedFrom: DateTimeOffset.UnixEpoch.AddTicks(1), SubmittedThrough: DateTimeOffset.UnixEpoch) })
        {
            var create = () => new ConversationSearch("marker", range: range);
            create.Should().Throw<ArgumentException>();
        }
    }

    [Theory]
    [InlineData("+1", "", "", "")]
    [InlineData(" 1", "", "", "")]
    [InlineData("", "1 ", "", "")]
    [InlineData("1.0", "", "", "")]
    [InlineData("9223372036854775808", "", "", "")]
    [InlineData("", "", "1970-01-01T00:00:00Z", "")]
    [InlineData("", "", "1970-01-01T00:00:00.0000000-00:00", "")]
    [InlineData("", "", "1970-01-01T01:00:00.0000000+01:00", "")]
    [InlineData("", "", "", "1970-01-01T00:00:00.0000000+00:00 ")]
    [InlineData("", "", "", "unknown")]
    public void StrictInputNeverRepairsInvalidBounds(string minimum, string maximum, string from, string through)
    {
        var parse = () => ConversationRangeFilter.Parse(minimum, maximum, from, through);
        parse.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void ParsedCriteriaBindSemanticDigestCursorAndCompletePage()
    {
        var time = DateTimeOffset.UnixEpoch.ToString("O", CultureInfo.InvariantCulture);
        var range = ConversationRangeFilter.Parse("02", "002", time, time);
        range.Should().Be(new ConversationRangeFilter(2, 2, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));
        ConversationRangeFilter.Parse("", "", "", "").Should().Be(default(ConversationRangeFilter));
        var search = new ConversationSearch("MARKER marker", range: range);
        search.Digest.Should().Be(new ConversationSearch("marker", range: range).Digest);
        foreach (var changed in new[] { range with { MinimumSequence = 1 }, range with { MaximumSequence = 3 },
            range with { SubmittedFrom = DateTimeOffset.MinValue }, range with { SubmittedThrough = DateTimeOffset.MaxValue }, default })
        { search.Digest.Should().NotBe(new ConversationSearch("marker", range: changed).Digest); }
        search.Digest.Should().NotBe(new ConversationSearch("marker", ConversationSearchRole.USER, range: range).Digest);
        search.Digest.Should().NotBe(new ConversationSearch("marker", terminal: new(ModelTurnOutcome.Denied), range: range).Digest);
        var cursor = new ConversationSearchCursor(new(Message().Request.SessionId, new(1), 3, 2, 6), search.Digest, Range: range);
        var page = new ConversationSearchPage(new(Message().Request.SessionId, new(1), true), 6, 3, search.Digest,
            [Message()], 2, cursor, Range: range);
        page.Range.Should().Be(range);
        page.Next!.Range.Should().Be(range);
        ConversationSearchPage.Serialize(page).Length.Should().BeLessThanOrEqualTo(ConversationText.MaximumPageUtf8Bytes);
    }

    [Theory]
    [InlineData("body")]
    [InlineData("sequence")]
    [InlineData("status")]
    [InlineData("time")]
    public void ExcludedCandidatesStillRequireFullMessageValidation(string invalid)
    {
        var message = invalid switch
        {
            "body" => Message() with { Body = "\ud800" },
            "sequence" => Message() with { Sequence = 0 },
            "status" => Message() with { Reason = (ModelTurnReason)999 },
            _ => Message() with { SubmittedAt = DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(1)) },
        };
        var match = () => new ConversationSearch("absent", ConversationSearchRole.HOST,
            new(ModelTurnOutcome.Denied), new(99)).Matches(message, TestContext.Current.CancellationToken);
        match.Should().Throw<Exception>();
    }

    private static ConversationMessage Message() => new(
        new(new(new Guid("11111111-1111-1111-1111-111111111111")), new(new Guid("22222222-2222-2222-2222-222222222222")),
            new(new Guid("33333333-3333-3333-3333-333333333333")), RequestOrigin.LocalUi),
        new(1), 2, 2, DateTimeOffset.UnixEpoch, "marker Denied", ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel);
}
