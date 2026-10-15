using System.Globalization;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Context;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SessionAttachmentMetadataBoundsTests
{
    [Fact]
    public void EveryStatusAndInclusiveRangeComposesWithEveryExistingPredicate()
    {
        foreach (var status in Enum.GetValues<AttachmentCopyStatus>().Cast<AttachmentCopyStatus?>().Prepend(null))
        foreach (var copy in Enum.GetValues<AttachmentCopyStatus>())
        foreach (var state in Enum.GetValues<AttachmentSessionState>())
        foreach (var filter in Enum.GetValues<SessionAttachmentFilter>())
        foreach (var removed in new[] { false, true })
        foreach (var exact in new[] { false, true })
        foreach (var media in new string?[] { null, "text/plain", "text/markdown" })
        foreach (var literal in new string?[] { null, "text/", "TEXT/" })
        {
            if (state == AttachmentSessionState.Removed && copy != AttachmentCopyStatus.Unavailable) { continue; }
            var row = SessionAttachmentSummaryTests.Summary() with
            {
                CopyStatus = copy, SessionState = state, MediaType = "text/plain",
                StoredBytes = copy == AttachmentCopyStatus.Unavailable ? 0 : 4
            };
            row.Validate();
            foreach (var (minimum, maximum) in new (long?, long?)[]
            {
                (null, null), (0, null), (null, 0), (0, 0), (4, null), (5, null),
                (null, 3), (null, 4), (4, 4), (0, LocalFilePolicy.MaximumBytes),
                (LocalFilePolicy.MaximumBytes, LocalFilePolicy.MaximumBytes)
            })
            {
                var query = new SessionAttachmentQuery(removed, exact ? row.Session.Value.ToString("D") : null,
                    filter, literal, media, status, minimum, maximum);
                var retained = copy != AttachmentCopyStatus.Unavailable;
                var matches = (removed || retained) && (status is null || copy == status)
                    && (filter == SessionAttachmentFilter.All || string.Equals(filter.ToString(), state.ToString(), StringComparison.Ordinal))
                    && (media is null || retained && string.Equals(media, row.MediaType, StringComparison.Ordinal))
                    && (literal is null || retained && string.Equals(literal, "text/", StringComparison.Ordinal))
                    && (minimum is null && maximum is null || retained
                        && (minimum is null || row.StoredBytes >= minimum) && (maximum is null || row.StoredBytes <= maximum));
                query.Matches(row).Should().Be(matches);
                row.RetainedStoredBytes.Should().Be(retained ? 4 : null);
                var page = new SessionAttachmentPage(query, matches ? [row] : [], null,
                    DateTimeOffset.UnixEpoch, 1, new string('a', 64), 1, false);
                page.Validate();
                if (!matches)
                { (page with { Records = [row] }).Invoking(value => value.Validate()).Should().Throw<InvalidDataException>(); }
            }
        }
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(LocalFilePolicy.MaximumBytes)]
    public void ZeroAndMaximumRetainedSizesMatchClosedBoundsAndRoundTripEveryStatus(long bytes)
    {
        foreach (var copy in Enum.GetValues<AttachmentCopyStatus>())
        {
            var row = SessionAttachmentSummaryTests.Summary() with
            { CopyStatus = copy, StoredBytes = copy == AttachmentCopyStatus.Unavailable ? 0 : bytes };
            var query = new SessionAttachmentQuery(true, copyStatus: copy, minimumStoredBytes: bytes, maximumStoredBytes: bytes);
            query.Matches(row).Should().Be(copy != AttachmentCopyStatus.Unavailable);
            JsonSerializer.Deserialize<SessionAttachmentQuery>(JsonSerializer.Serialize(query)).Should().Be(query);
            (query == new SessionAttachmentQuery(true, minimumStoredBytes: bytes, maximumStoredBytes: bytes)).Should().BeFalse();
        }
    }

    [Theory]
    [InlineData(-1L, null)]
    [InlineData(262145L, null)]
    [InlineData(null, -1L)]
    [InlineData(null, 262145L)]
    [InlineData(1L, 0L)]
    [InlineData(long.MaxValue, null)]
    [InlineData(null, long.MaxValue)]
    public void InvalidTypedBoundsFailAtConstructionValidationMatchingAndPage(long? minimum, long? maximum)
    {
        var create = () => new SessionAttachmentQuery(minimumStoredBytes: minimum, maximumStoredBytes: maximum);
        create.Should().Throw<InvalidDataException>();
        var query = new SessionAttachmentQuery();
        typeof(SessionAttachmentQuery).GetProperty(nameof(SessionAttachmentQuery.MinimumStoredBytes))!.SetValue(query, minimum);
        typeof(SessionAttachmentQuery).GetProperty(nameof(SessionAttachmentQuery.MaximumStoredBytes))!.SetValue(query, maximum);
        query.Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
        query.Invoking(value => value.Matches(SessionAttachmentSummaryTests.Summary())).Should().Throw<InvalidDataException>();
        new SessionAttachmentPage(query, [], null, DateTimeOffset.UnixEpoch, 1, new string('a', 64), 0, false)
            .Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public void UnknownStatusNeverMeansAny(int status)
    {
        var create = () => new SessionAttachmentQuery(copyStatus: (AttachmentCopyStatus)status);
        create.Should().Throw<InvalidDataException>();
        var query = new SessionAttachmentQuery();
        typeof(SessionAttachmentQuery).GetProperty(nameof(SessionAttachmentQuery.CopyStatus))!.SetValue(query, (AttachmentCopyStatus)status);
        query.Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
        query.Invoking(value => value.Matches(SessionAttachmentSummaryTests.Summary())).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(" ")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("+1")]
    [InlineData("-0")]
    [InlineData("1,000")]
    [InlineData("1_000")]
    [InlineData("1.0")]
    [InlineData("1e2")]
    [InlineData("0x1")]
    [InlineData("1\n")]
    [InlineData("1\0")]
    [InlineData("\t1")]
    [InlineData("１")]
    [InlineData("١")]
    [InlineData("\uD800")]
    [InlineData("9223372036854775808")]
    public void SharedNumericPolicyRejectsNonemptyNonUnsignedDecimal(string text) =>
        ((Action)(() => OptionalUnsignedDecimal.Parse(text))).Should().Throw<InvalidDataException>();

    [Fact]
    public void SharedParserPreservesEmptyAndLeadingZeroSemanticsAndNeverClamps()
    {
        ((Action)(() => OptionalUnsignedDecimal.Parse(null!))).Should().Throw<ArgumentNullException>();
        OptionalUnsignedDecimal.Parse("").Should().BeNull();
        OptionalUnsignedDecimal.Parse("0000").Should().Be(0);
        OptionalUnsignedDecimal.Parse("000262144").Should().Be(LocalFilePolicy.MaximumBytes);
        OptionalUnsignedDecimal.Parse(long.MaxValue.ToString(CultureInfo.InvariantCulture)).Should().Be(long.MaxValue);
        var create = () => new SessionAttachmentQuery(minimumStoredBytes: OptionalUnsignedDecimal.Parse("262145"));
        create.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void FilteredOutSummariesAreValidatedBeforeAnyExclusionAndSerializedFieldsMustBeExplicit()
    {
        var query = new SessionAttachmentQuery(copyStatus: AttachmentCopyStatus.Held, minimumStoredBytes: 1);
        var invalid = SessionAttachmentSummaryTests.Summary() with { StoredBytes = -1 };
        query.Invoking(value => value.Matches(invalid)).Should().Throw<InvalidDataException>();
        foreach (var field in new[] { "CopyStatus", "MinimumStoredBytes", "MaximumStoredBytes" })
        {
            var json = JsonSerializer.SerializeToNode(new SessionAttachmentQuery())!.AsObject();
            json.Remove(field);
            ((Action)(() => JsonSerializer.Deserialize<SessionAttachmentQuery>(json.ToJsonString())))
                .Should().Throw<JsonException>();
        }
    }
}
