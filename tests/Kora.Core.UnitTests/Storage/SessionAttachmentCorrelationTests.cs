using System.Globalization;
using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Context;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SessionAttachmentCorrelationTests
{
    [Fact]
    public void ExactCorrelationIdsReuseCanonicalSessionIdAdmissionWithoutRepair()
    {
        foreach (var id in new[] { "", " ", Guid.Empty.ToString("D"), Guid.NewGuid().ToString("N"),
            "{aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa}", "AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA",
            " aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\n",
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaａ", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa\u200b" })
        {
            ((Action)(() => _ = new SessionAttachmentQuery(exactObservationSource: id))).Should().Throw<InvalidDataException>();
            ((Action)(() => _ = new SessionAttachmentQuery(exactDeviceProfile: id))).Should().Throw<InvalidDataException>();
        }
        var row = SessionAttachmentSummaryTests.Summary();
        new SessionAttachmentQuery(exactObservationSource: row.ObservationSource.ToString("D"),
            exactDeviceProfile: row.Profile.Value.ToString("D")).Matches(row).Should().BeTrue();
    }

    [Fact]
    public void UtcParsingAndTypedRangesRejectNoncanonicalOrReversedBounds()
    {
        ((Action)(() => OptionalCanonicalUtc.Parse(null!))).Should().Throw<ArgumentNullException>();
        OptionalCanonicalUtc.Parse("").Should().BeNull();
        foreach (var text in new[] { " ", "1970-01-01T00:00:00Z", "1970-01-01T00:00:00.0000000Z",
            "1970-01-01T00:00:00+00:00", "1970-01-01T00:00:00.0000000-00:00",
            "1970-01-01T01:00:00.0000000+01:00", "1970-01-01T00:00:00.0000000+00:00 ",
            "1970-01-01t00:00:00.0000000+00:00", "2026-02-30T00:00:00.0000000+00:00" })
        { ((Action)(() => OptionalCanonicalUtc.Parse(text))).Should().Throw<InvalidDataException>(); }
        foreach (var time in new[] { DateTimeOffset.MinValue, DateTimeOffset.UnixEpoch, DateTimeOffset.MaxValue })
        { OptionalCanonicalUtc.Parse(time.ToString("O", CultureInfo.InvariantCulture)).Should().Be(time); }
        var utc = DateTimeOffset.UnixEpoch;
        var offset = utc.ToOffset(TimeSpan.FromHours(1));
        foreach (var (from, until) in new (DateTimeOffset?, DateTimeOffset?)[] {
            (offset, null), (null, offset), (utc.AddTicks(1), utc) })
        {
            ((Action)(() => _ = new SessionAttachmentQuery(capturedFromUtc: from, capturedUntilUtc: until)))
                .Should().Throw<InvalidDataException>();
        }
    }

    [Fact]
    public void CorrelationPredicatesAreInclusiveAndConjunctiveOverOnlyCertifiedAvailableCopies()
    {
        var row = SessionAttachmentSummaryTests.Summary();
        foreach (var state in Enum.GetValues<AttachmentSessionState>())
        foreach (var copy in Enum.GetValues<AttachmentCopyStatus>())
        foreach (var source in new string?[] { null, row.ObservationSource.ToString("D"), Guid.NewGuid().ToString("D") })
        foreach (var profile in new string?[] { null, row.Profile.Value.ToString("D"), Guid.NewGuid().ToString("D") })
        foreach (var from in new DateTimeOffset?[] { null, row.CapturedAt, row.CapturedAt.AddTicks(1) })
        foreach (var until in new DateTimeOffset?[] { null, row.CapturedAt, row.CapturedAt.AddTicks(-1) })
        {
            if (state == AttachmentSessionState.Removed && copy != AttachmentCopyStatus.Unavailable || from > until) { continue; }
            var candidate = row with { SessionState = state, CopyStatus = copy,
                StoredBytes = copy == AttachmentCopyStatus.Unavailable ? 0 : row.StoredBytes };
            var query = new SessionAttachmentQuery(true, exactObservationSource: source, exactDeviceProfile: profile,
                capturedFromUtc: from, capturedUntilUtc: until);
            var expected = !query.HasCorrelationPredicates || copy == AttachmentCopyStatus.Confirmed
                && (source is null || string.Equals(source, row.ObservationSource.ToString("D"), StringComparison.Ordinal))
                && (profile is null || string.Equals(profile, row.Profile.Value.ToString("D"), StringComparison.Ordinal))
                && (from is null || from <= row.CapturedAt) && (until is null || until >= row.CapturedAt);
            query.Matches(candidate).Should().Be(expected);
        }
        foreach (var includeRemoved in new[] { false, true })
        foreach (var exact in new[] { false, true })
        foreach (var lifecycle in Enum.GetValues<SessionAttachmentFilter>())
        foreach (var literal in new string?[] { null, "text/", "TEXT/" })
        foreach (var media in new string?[] { null, LocalFileMediaType.Markdown, LocalFileMediaType.PlainText })
        foreach (var status in new AttachmentCopyStatus?[] { null, AttachmentCopyStatus.Confirmed, AttachmentCopyStatus.Held, AttachmentCopyStatus.Unavailable })
        foreach (var digest in new string?[] { null, row.File.Digest, new string('b', 64) })
        foreach (var bound in new long?[] { null, row.StoredBytes, row.StoredBytes + 1 })
        {
            var query = new SessionAttachmentQuery(includeRemoved, exact ? row.Session.Value.ToString("D") : null,
                lifecycle, literal, media, status, bound, null, digest, row.ObservationSource.ToString("D"),
                row.Profile.Value.ToString("D"), row.CapturedAt, row.CapturedAt);
            var baseline = new SessionAttachmentQuery(includeRemoved, exact ? row.Session.Value.ToString("D") : null,
                lifecycle, literal, media, status, bound, null, digest);
            query.Matches(row).Should().Be(baseline.Matches(row));
        }
    }

    [Fact]
    public void NonmatchingCorrelationCannotHideInvalidCandidatesOrDiscloseTombstoneMetadata()
    {
        var row = SessionAttachmentSummaryTests.Summary();
        var query = new SessionAttachmentQuery(true, exactObservationSource: Guid.NewGuid().ToString("D"),
            exactDeviceProfile: Guid.NewGuid().ToString("D"), capturedFromUtc: row.CapturedAt.AddTicks(1));
        foreach (var invalid in new[] { row with { ObservationSource = Guid.Empty }, row with { Profile = default },
            row with { CapturedAt = row.CapturedAt.ToOffset(TimeSpan.FromHours(1)) },
            row with { File = row.File with { Digest = new string('A', 64) } },
            row with { CopyStatus = (AttachmentCopyStatus)99 } })
        { query.Invoking(value => value.Matches(invalid)).Should().Throw<InvalidDataException>(); }
        foreach (var state in new[] { AttachmentSessionState.Active, AttachmentSessionState.Done, AttachmentSessionState.Removed, (AttachmentSessionState)99 })
        foreach (var copy in Enum.GetValues<AttachmentCopyStatus>())
        {
            var candidate = row with { SessionState = state, CopyStatus = copy, StoredBytes = 0 };
            var available = copy != AttachmentCopyStatus.Unavailable && state is AttachmentSessionState.Active or AttachmentSessionState.Done;
            candidate.RetainedObservationSource.Should().Be(available ? row.ObservationSource : null);
            candidate.RetainedProfile.Should().Be(available ? row.Profile : null);
            candidate.RetainedCapturedAt.Should().Be(available ? row.CapturedAt : null);
            if (!available)
            {
                var json = JsonSerializer.Serialize(candidate);
                json.Should().NotContain(row.ObservationSource.ToString("D")).And.NotContain(row.Profile.Value.ToString("D"))
                    .And.NotContain(row.CapturedAt.ToString("O", CultureInfo.InvariantCulture));
            }
        }
    }

    [Fact]
    public void PrivateQueryIdentityAndCompleteBoundedEmptyPagesIncludeEveryCorrelationPredicate()
    {
        var row = SessionAttachmentSummaryTests.Summary();
        var baseline = new SessionAttachmentQuery(true);
        foreach (var query in new[] {
            new SessionAttachmentQuery(true, exactObservationSource: row.ObservationSource.ToString("D")),
            new(true, exactDeviceProfile: row.Profile.Value.ToString("D")),
            new(true, capturedFromUtc: row.CapturedAt), new(true, capturedUntilUtc: row.CapturedAt) })
        {
            query.Should().NotBe(baseline);
            JsonSerializer.Serialize(query).Should().Be(JsonSerializer.Serialize(baseline));
            JsonSerializer.Deserialize<SessionAttachmentQuery>(JsonSerializer.Serialize(query))!.Should().Be(baseline);
            var page = new SessionAttachmentPage(query, [], "YQ==." + new string('b', 64),
                DateTimeOffset.UnixEpoch, 1, new string('b', 64), 64, true);
            page.Validate();
            (page with { Next = null, ScanLimitReached = false }).Validate();
            (page with { Records = [row] }).Validate();
            (page with { Records = [row with { CopyStatus = AttachmentCopyStatus.Held }] })
                .Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
        }
    }
}
