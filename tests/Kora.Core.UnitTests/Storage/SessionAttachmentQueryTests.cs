using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SessionAttachmentQueryTests
{
    [Fact]
    public void LiteralMetadataMatchesOnlyExposedIdsDigestAndMediaTypeWithoutSerialization()
    {
        var row = SessionAttachmentSummaryTests.Summary();
        var fields = new[] { row.Session.Value.ToString("D"), row.File.SourceId.ToString("D"),
            row.File.RevisionId.ToString("D"), row.File.ItemId.ToString("D"), row.File.Digest, row.MediaType };
        foreach (var field in fields)
        {
            var text = field[..8];
            var query = new SessionAttachmentQuery(true, row.Session.Value.ToString("D"),
                SessionAttachmentFilter.Done, text);
            query.Matches(row).Should().BeTrue();
            query.Matches(row with { CopyStatus = AttachmentCopyStatus.Held }).Should().BeTrue();
            query.Matches(row with { CopyStatus = AttachmentCopyStatus.Unavailable, StoredBytes = 0 }).Should().BeFalse();
            query.Matches(row with { SessionState = AttachmentSessionState.Active }).Should().BeFalse();
            query.Matches(row with { Session = new(Guid.NewGuid()) }).Should().BeFalse();
            query.MetadataText.Should().Be(text);
            var json = JsonSerializer.Serialize(query);
            json.Should().NotContain("MetadataText");
            JsonSerializer.Deserialize<SessionAttachmentQuery>(json)!.MetadataText.Should().BeNull();
            (query == new SessionAttachmentQuery(true, query.ExactSessionId, query.Filter, text + "x")).Should().BeFalse();
        }
        foreach (var text in new[] { "TEXT/", " text/", "text/ ", "*", "%", "_", "Synthetic",
            "private content", row.Profile.Value.ToString("D"), row.ObservationSource.ToString("D") })
        {
            new SessionAttachmentQuery(metadataText: text).Matches(row).Should().BeFalse();
        }
        foreach (var text in new[] { new string('a', 256), new string('é', 256),
            string.Concat(Enumerable.Repeat("😀", 128)), " " })
        {
            new SessionAttachmentQuery(metadataText: text).Validate();
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("\t")]
    [InlineData("\u200b")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    public void LiteralMetadataRejectsInvalidInputRatherThanBrowsing(string text) =>
        ((Action)(() => _ = new SessionAttachmentQuery(metadataText: text))).Should().Throw<InvalidDataException>();

    [Fact]
    public void LiteralMetadataRejectsSizeAndMalformedUnicodeAndAllowsFiniteEmptyPages()
    {
        foreach (var text in new[] { new string('a', 257), new string('界', 171), new string('\ud800', 1) })
        {
            ((Action)(() => _ = new SessionAttachmentQuery(metadataText: text))).Should().Throw<InvalidDataException>();
        }
        var row = SessionAttachmentSummaryTests.Summary();
        var page = new SessionAttachmentPage(new(true, metadataText: "unmatched"), [], "YQ==." + new string('a', 64),
            DateTimeOffset.UnixEpoch, 1, new string('a', 64), 64, true);
        page.Validate();
        (page with { Records = [row] }).Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
        (page with { Next = null, ScanLimitReached = false }).Validate();
    }

    [Fact]
    public void MalformedInMemoryFilterIsRejectedEvenOnAnEmptyPage()
    {
        var query = new SessionAttachmentQuery(true);
        typeof(SessionAttachmentQuery).GetProperty(nameof(SessionAttachmentQuery.Filter))!
            .SetValue(query, (SessionAttachmentFilter)99);
        query.Invoking(value => value.Matches(SessionAttachmentSummaryTests.Summary()))
            .Should().Throw<InvalidDataException>();
        var page = new SessionAttachmentPage(query, [], null, DateTimeOffset.UnixEpoch, 1, new string('a', 64), 0, false);
        page.Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(SessionAttachmentFilter.Active)]
    [InlineData(SessionAttachmentFilter.Done)]
    [InlineData(SessionAttachmentFilter.Removed)]
    public void LifecycleBrowseAllowsEmptyScansWithRemovedSlotsAndContinuation(SessionAttachmentFilter filter)
    {
        var query = new SessionAttachmentQuery(true, filter: filter);
        var cursor = "YQ==." + new string('a', 64);
        var page = new SessionAttachmentPage(query, [], cursor, DateTimeOffset.UnixEpoch, 1, new string('a', 64), 64, true);
        page.Validate();
        (page with { Query = new(true) }).Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(SessionAttachmentFilter.All)]
    [InlineData(SessionAttachmentFilter.Active)]
    [InlineData(SessionAttachmentFilter.Done)]
    [InlineData(SessionAttachmentFilter.Removed)]
    public void LifecycleFilterRoundTripsAndCombinesIndependentlyWithExactIdAndRemovedSlots(SessionAttachmentFilter filter)
    {
        var original = SessionAttachmentSummaryTests.Summary();
        foreach (var state in Enum.GetValues<AttachmentSessionState>())
        foreach (var includeRemoved in new[] { false, true })
        foreach (var removedSlot in new[] { false, true })
        {
            if (state == AttachmentSessionState.Removed && !removedSlot) { continue; }
            var row = original with { SessionState = state, CopyStatus = removedSlot
                ? AttachmentCopyStatus.Unavailable : AttachmentCopyStatus.Confirmed, StoredBytes = removedSlot ? 0 : original.StoredBytes };
            row.Validate();
            var query = new SessionAttachmentQuery(includeRemoved, row.Session.Value.ToString("D"), filter);
            JsonSerializer.Deserialize<SessionAttachmentQuery>(JsonSerializer.Serialize(query)).Should().Be(query);
            var matchesState = filter == SessionAttachmentFilter.All
                || string.Equals(filter.ToString(), state.ToString(), StringComparison.Ordinal);
            query.Matches(row).Should().Be(matchesState && (includeRemoved || !removedSlot));
            query.Matches(row with { Session = new(Guid.NewGuid()) }).Should().BeFalse();
            var page = new SessionAttachmentPage(query, [], null, DateTimeOffset.UnixEpoch, 1, new string('a', 64), 64, false);
            page.Validate();
            if (!query.Matches(row))
            {
                (page with { Records = [row] }).Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
            }
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(int.MaxValue)]
    public void UnknownLifecycleFilterFailsWithoutDefaultFallback(int filter)
    {
        ((Action)(() => _ = new SessionAttachmentQuery(filter: (SessionAttachmentFilter)filter)))
            .Should().Throw<InvalidDataException>();
        ((Action)(() => JsonSerializer.Deserialize<SessionAttachmentQuery>(
            "{\"IncludeRemoved\":false,\"ExactSessionId\":null,\"Filter\":" + filter + "}")))
            .Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("ABCDEFAB-1234-5678-9012-abcdefabcdef")]
    [InlineData("abcdefab123456789012abcdefabcdef")]
    [InlineData("{abcdefab-1234-5678-9012-abcdefabcdef}")]
    [InlineData(" abcdefab-1234-5678-9012-abcdefabcdef")]
    [InlineData("abcdefab-1234-5678-9012-abcdefabcdef\n")]
    [InlineData("abcdefab-1234-5678-9012-abcdefabcdef\u200D")]
    public void ExactQueryRejectsNoncanonicalInputWithoutBrowseFallback(string value)
    {
        ((Action)(() => _ = new SessionAttachmentQuery(false, value))).Should().Throw<InvalidDataException>();
        ((Action)(() => _ = new SessionAttachmentQuery(true, value))).Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void OversizedExactQueryUsesAuthoritativeIdPolicy()
    {
        ((Action)(() => _ = new SessionAttachmentQuery(false, new string('a', 10000))))
            .Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void QueryRoundTripsRemovalFlagAndExactFilterWithoutSearchingOtherHeaders(bool includeRemoved, bool exact)
    {
        var row = SessionAttachmentSummaryTests.Summary();
        var text = exact ? row.Session.Value.ToString("D") : null;
        var query = new SessionAttachmentQuery(includeRemoved, text);
        query.IncludeRemoved.Should().Be(includeRemoved);
        query.ExactSessionId.Should().Be(text);
        query.ExactId.Should().Be(exact ? row.Session.Value : null);
        var restored = JsonSerializer.Deserialize<SessionAttachmentQuery>(JsonSerializer.Serialize(query));
        restored.Should().Be(query);
        query.Matches(row).Should().BeTrue();
        query.Matches(row with { Session = new(Guid.NewGuid()) }).Should().Be(!exact);
        query.Matches(row with { CopyStatus = AttachmentCopyStatus.Unavailable, StoredBytes = 0 })
            .Should().Be(includeRemoved);
        query.Matches(row with { CopyStatus = AttachmentCopyStatus.Held }).Should().BeTrue();
        (query == new SessionAttachmentQuery(!includeRemoved, text)).Should().BeFalse();
        (query == new SessionAttachmentQuery(includeRemoved, Guid.NewGuid().ToString("D"))).Should().BeFalse();
    }

    [Fact]
    public void ExactPageAllowsNonmatchingScansButRejectsDisclosureOfDifferentSession()
    {
        var row = SessionAttachmentSummaryTests.Summary();
        var query = new SessionAttachmentQuery(true, row.Session.Value.ToString("D"));
        var page = new SessionAttachmentPage(query, [row], null, DateTimeOffset.UnixEpoch,
            1, new string('a', 64), 64, false);
        page.Validate();
        (page with { Records = [] }).Validate();
        (page with { Records = [row with { Session = new(Guid.NewGuid()) }] })
            .Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
    }
}
