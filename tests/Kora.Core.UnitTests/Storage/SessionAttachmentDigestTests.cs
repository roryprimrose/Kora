using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Context;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SessionAttachmentDigestTests
{
    [Fact]
    public void StrictDigestSyntaxIsSharedWithFileReferencesAndNeverNormalized()
    {
        foreach (var value in new string?[] { null, "", " ", new('0', 64), new('a', 63), new('a', 65),
            new('A', 64), new('g', 64), " " + new string('a', 64), new string('a', 64) + "\n",
            new string('a', 63) + "\0", new string('a', 63) + "ａ", new string('a', 63) + "\u200b" })
        {
            LocalFileDigest.IsCanonical(value).Should().BeFalse();
            ((Action)(() => LocalFileDigest.Validate(value!))).Should().Throw<InvalidDataException>();
            (SessionAttachmentSummaryTests.Summary().File with { Digest = value! })
                .Invoking(file => file.Validate()).Should().Throw<InvalidDataException>();
            if (value is not null)
            {
                ((Action)(() => _ = new SessionAttachmentQuery(exactDigest: value))).Should().Throw<InvalidDataException>();
            }
        }
        foreach (var value in new[] { new string('a', 64), new string('0', 63) + "1",
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" })
        {
            LocalFileDigest.IsCanonical(value).Should().BeTrue();
            LocalFileDigest.Validate(value);
            new SessionAttachmentQuery(exactDigest: value).Validate();
        }
    }

    [Fact]
    public void ExactDigestAndEveryExistingPredicateComposeOnlyOverValidatedConfirmedCopies()
    {
        var original = SessionAttachmentSummaryTests.Summary();
        foreach (var state in Enum.GetValues<AttachmentSessionState>())
        foreach (var copy in Enum.GetValues<AttachmentCopyStatus>())
        foreach (var includeRemoved in new[] { false, true })
        foreach (var exact in new[] { false, true })
        foreach (var filter in Enum.GetValues<SessionAttachmentFilter>())
        foreach (var literal in new string?[] { null, "text/", "TEXT/" })
        foreach (var mediaType in new string?[] { null, "text/markdown", "text/plain" })
        foreach (var status in new AttachmentCopyStatus?[] { null, AttachmentCopyStatus.Confirmed, AttachmentCopyStatus.Held, AttachmentCopyStatus.Unavailable })
        {
            if (state == AttachmentSessionState.Removed && copy != AttachmentCopyStatus.Unavailable) { continue; }
            var row = original with { SessionState = state, CopyStatus = copy, StoredBytes = copy == AttachmentCopyStatus.Unavailable ? 0 : 1 };
            var query = new SessionAttachmentQuery(includeRemoved, exact ? row.Session.Value.ToString("D") : null,
                filter, literal, mediaType, status, 1, 1, row.File.Digest);
            var expected = copy == AttachmentCopyStatus.Confirmed
                && (filter == SessionAttachmentFilter.All || string.Equals(filter.ToString(), state.ToString(), StringComparison.Ordinal))
                && !string.Equals(literal, "TEXT/", StringComparison.Ordinal)
                && !string.Equals(mediaType, "text/plain", StringComparison.Ordinal)
                && (status is null or AttachmentCopyStatus.Confirmed);
            query.Matches(row).Should().Be(expected);
            query.Matches(row with { File = row.File with { Digest = new string('b', 64) } }).Should().BeFalse();
            query.Matches(row with { Session = new(Guid.NewGuid()) }).Should().Be(expected && !exact);
        }
        foreach (var (minimum, maximum) in new (long?, long?)[] { (null, null), (0, null), (null, 1), (1, 1), (2, null), (null, 0) })
        {
            var query = new SessionAttachmentQuery(true, minimumStoredBytes: minimum, maximumStoredBytes: maximum,
                exactDigest: original.File.Digest);
            var row = original with { StoredBytes = 1 };
            query.Matches(row).Should().Be((minimum is null || minimum <= 1) && (maximum is null || maximum >= 1));
        }
    }

    [Fact]
    public void InvalidCandidatesFailBeforeAnyNonmatchingFilterAndRemovedObjectsNeverDisclosePriorDigest()
    {
        var row = SessionAttachmentSummaryTests.Summary();
        foreach (var copy in Enum.GetValues<AttachmentCopyStatus>())
        {
            var candidate = row with { CopyStatus = copy, StoredBytes = copy == AttachmentCopyStatus.Unavailable ? 0 : 1 };
            foreach (var malformed in new[] { candidate with { ObservationSource = Guid.Empty },
                candidate with { File = candidate.File with { Digest = new string('A', 64) } } })
            {
                new SessionAttachmentQuery(false, Guid.NewGuid().ToString("D"), SessionAttachmentFilter.Removed,
                    "no match", "text/plain", AttachmentCopyStatus.Unavailable, 0, 0, new string('b', 64))
                    .Invoking(query => query.Matches(malformed)).Should().Throw<InvalidDataException>();
            }
        }
        foreach (var removed in new[] {
            row with { CopyStatus = AttachmentCopyStatus.Unavailable, StoredBytes = 0 },
            row with { CopyStatus = AttachmentCopyStatus.Unavailable },
            row with { SessionState = AttachmentSessionState.Removed },
            row with { SessionState = AttachmentSessionState.Removed, CopyStatus = AttachmentCopyStatus.Held },
            row with { SessionState = (AttachmentSessionState)99 },
            row with { CopyStatus = (AttachmentCopyStatus)99 } })
        {
            removed.RetainedDigest.Should().BeNull();
            removed.FileMetadata.Digest.Should().BeNull();
            var json = JsonSerializer.Serialize(removed);
            json.Should().NotContain(row.File.Digest);
            json.Should().Contain(row.File.SourceId.ToString("D"));
            JsonSerializer.Serialize(new SessionAttachmentPage(new(true), [removed], null,
                DateTimeOffset.UnixEpoch, 1, new string('b', 64), 1, false)).Should().NotContain(row.File.Digest);
        }
        row.RetainedDigest.Should().Be(row.File.Digest);
        (row with { CopyStatus = AttachmentCopyStatus.Held, SessionState = AttachmentSessionState.Active })
            .RetainedDigest.Should().Be(row.File.Digest);
        row.FileMetadata.Digest.Should().Be(row.File.Digest);
        JsonSerializer.Serialize(row).Should().Contain(row.File.Digest);
    }

    [Fact]
    public void DigestQueryIsVolatileAndEmptyFilteredPagesRemainValidButNonmatchingDisclosureFails()
    {
        var row = SessionAttachmentSummaryTests.Summary();
        var query = new SessionAttachmentQuery(true, exactDigest: row.File.Digest);
        var json = JsonSerializer.Serialize(query);
        json.Should().NotContain("ExactDigest").And.NotContain(row.File.Digest);
        JsonSerializer.Deserialize<SessionAttachmentQuery>(json)!.ExactDigest.Should().BeNull();
        query.Should().NotBe(new SessionAttachmentQuery(true));
        query.Should().NotBe(new SessionAttachmentQuery(true, exactDigest: new string('b', 64)));
        var page = new SessionAttachmentPage(query, [], "YQ==." + new string('b', 64),
            DateTimeOffset.UnixEpoch, 1, new string('b', 64), 64, true);
        page.Validate();
        (page with { Next = null, ScanLimitReached = false }).Validate();
        (page with { Records = [row] }).Validate();
        (page with { Records = [row with { CopyStatus = AttachmentCopyStatus.Held }] })
            .Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
    }
}
