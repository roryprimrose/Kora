using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SessionAttachmentVersionRangeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void PositiveHostRevisionAndStrictDecimalAdmissionRejectInvalidOrInvertedBounds(int field)
    {
        foreach (var text in new[] { "0", "-1", "+1", " 1", "1 ", "1\n", "1.0", "1,000", "１", "١",
            "1\u200b", "9223372036854775808" })
        {
            ((Action)(() => Query(field, OptionalUnsignedDecimal.Parse(text), null))).Should().Throw<InvalidDataException>();
            ((Action)(() => Query(field, null, OptionalUnsignedDecimal.Parse(text)))).Should().Throw<InvalidDataException>();
        }
        foreach (var invalid in new long[] { -1, 0, long.MinValue })
        {
            ((Action)(() => Query(field, invalid, null))).Should().Throw<InvalidDataException>();
            ((Action)(() => Query(field, null, invalid))).Should().Throw<InvalidDataException>();
        }
        ((Action)(() => Query(field, 2, 1))).Should().Throw<InvalidDataException>();
        foreach (var valid in new[] { "1", "01", "9223372036854775807" })
        {
            var bound = OptionalUnsignedDecimal.Parse(valid);
            Query(field, bound, bound).Validate();
        }
        Query(field, OptionalUnsignedDecimal.Parse(""), null).HasVersionPredicates.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RangesAreInclusiveOptionalAndOnlyMatchCertifiedAvailableMetadata(int field)
    {
        var row = SessionAttachmentSummaryTests.Summary() with
        { SessionGeneration = new(7), CapturedGeneration = new(5), StorageRevision = new(9) };
        var value = field switch { 0 => 7, 1 => 5, _ => 9 };
        foreach (var minimum in new long?[] { null, 1, value, value + 1, long.MaxValue })
        foreach (var maximum in new long?[] { null, 1, value - 1, value, long.MaxValue })
        foreach (var state in Enum.GetValues<AttachmentSessionState>())
        foreach (var copy in Enum.GetValues<AttachmentCopyStatus>())
        {
            if (minimum > maximum || state == AttachmentSessionState.Removed && copy != AttachmentCopyStatus.Unavailable) { continue; }
            var query = Query(field, minimum, maximum);
            var candidate = row with { SessionState = state, CopyStatus = copy,
                StoredBytes = copy == AttachmentCopyStatus.Unavailable ? 0 : row.StoredBytes };
            var expected = minimum is null && maximum is null
                || copy == AttachmentCopyStatus.Confirmed && (minimum is null || value >= minimum) && (maximum is null || value <= maximum);
            query.Matches(candidate).Should().Be(expected);
        }
        var extreme = row with { SessionGeneration = new(long.MaxValue), CapturedGeneration = new(long.MaxValue), StorageRevision = new(long.MaxValue) };
        Query(field, long.MaxValue, long.MaxValue).Matches(extreme).Should().BeTrue();
    }

    [Fact]
    public void AllRangesComposeWithEveryExistingPredicateAndCannotHideInvalidCandidates()
    {
        var row = SessionAttachmentSummaryTests.Summary();
        foreach (var includeRemoved in new[] { false, true })
        foreach (var exact in new[] { false, true })
        foreach (var lifecycle in Enum.GetValues<SessionAttachmentFilter>())
        foreach (var literal in new string?[] { null, "text/", "TEXT/" })
        foreach (var media in new string?[] { null, "text/plain", "text/markdown" })
        foreach (var status in new AttachmentCopyStatus?[] { null, AttachmentCopyStatus.Confirmed, AttachmentCopyStatus.Held })
        foreach (var bytes in new long?[] { null, row.StoredBytes, row.StoredBytes + 1 })
        foreach (var digest in new string?[] { null, row.File.Digest, new string('b', 64) })
        {
            var baseline = new SessionAttachmentQuery(includeRemoved, exact ? row.Session.Value.ToString("D") : null,
                lifecycle, literal, media, status, bytes, null, digest, row.ObservationSource.ToString("D"),
                row.Profile.Value.ToString("D"), row.CapturedAt, row.CapturedAt);
            var query = new SessionAttachmentQuery(includeRemoved, baseline.ExactSessionId, lifecycle, literal, media,
                status, bytes, null, digest, row.ObservationSource.ToString("D"), row.Profile.Value.ToString("D"),
                row.CapturedAt, row.CapturedAt, row.SessionGeneration.Value, row.SessionGeneration.Value,
                row.CapturedGeneration.Value, row.CapturedGeneration.Value, row.StorageRevision.Value, row.StorageRevision.Value);
            query.Matches(row).Should().Be(baseline.Matches(row));
        }
        for (var field = 0; field < 3; field++)
        {
            var query = Query(field, long.MaxValue, null);
            foreach (var invalid in new[] { row with { SessionGeneration = default }, row with { CapturedGeneration = default },
                row with { StorageRevision = default }, row with { CapturedGeneration = new(3) },
                row with { File = row.File with { Digest = "invalid" } }, row with { CopyStatus = (AttachmentCopyStatus)99 } })
            { query.Invoking(value => value.Matches(invalid)).Should().Throw<InvalidDataException>(); }
        }
        new SessionAttachmentQuery(minimumSessionGeneration: 1, minimumCapturedGeneration: 2).Matches(row).Should().BeFalse();
        new SessionAttachmentQuery(minimumSessionGeneration: 1, minimumStorageRevision: 2).Matches(row).Should().BeFalse();
    }

    [Fact]
    public void CompleteQueryIdentityAndBoundedPagesPreservePrivacyAndTombstoneRedaction()
    {
        var row = SessionAttachmentSummaryTests.Summary();
        for (var field = 0; field < 3; field++)
        foreach (var (lower, upper) in new (long?, long?)[] { (1, null), (null, 1), (1, 1) })
        {
            var query = Query(field, lower, upper);
            query.Should().NotBe(new SessionAttachmentQuery(true));
            Query(field, lower, upper).Should().Be(query);
            var serialized = JsonSerializer.Serialize(query);
            serialized.Should().NotContain("SessionGeneration").And.NotContain("CapturedGeneration").And.NotContain("StorageRevision");
            new SessionAttachmentPage(query, [], null, DateTimeOffset.UnixEpoch, 1, new string('b', 64), 1, false).Validate();
            new SessionAttachmentPage(query, [], "YQ==." + new string('b', 64), DateTimeOffset.UnixEpoch,
                1, new string('b', 64), 64, true).Validate();
        }
        foreach (var state in new[] { AttachmentSessionState.Active, AttachmentSessionState.Done, AttachmentSessionState.Removed, (AttachmentSessionState)99 })
        foreach (var copy in Enum.GetValues<AttachmentCopyStatus>())
        {
            var candidate = row with { SessionState = state, CopyStatus = copy, StoredBytes = 0,
                SessionGeneration = new(1234567), CapturedGeneration = new(1234566), StorageRevision = new(1234565) };
            var available = copy != AttachmentCopyStatus.Unavailable && state is AttachmentSessionState.Active or AttachmentSessionState.Done;
            candidate.RetainedSessionGeneration.Should().Be(available ? new HostRevision(1234567) : null);
            candidate.RetainedCapturedGeneration.Should().Be(available ? new HostRevision(1234566) : null);
            candidate.RetainedStorageRevision.Should().Be(available ? new HostRevision(1234565) : null);
            var json = JsonSerializer.Serialize(candidate);
            if (available) { json.Should().Contain("1234567").And.Contain("1234566").And.Contain("1234565"); }
            else { json.Should().NotContain("1234567").And.NotContain("1234566").And.NotContain("1234565"); }
        }
    }

    private static SessionAttachmentQuery Query(int field, long? minimum, long? maximum) => field switch
    {
        0 => new(true, minimumSessionGeneration: minimum, maximumSessionGeneration: maximum),
        1 => new(true, minimumCapturedGeneration: minimum, maximumCapturedGeneration: maximum),
        _ => new(true, minimumStorageRevision: minimum, maximumStorageRevision: maximum)
    };
}
