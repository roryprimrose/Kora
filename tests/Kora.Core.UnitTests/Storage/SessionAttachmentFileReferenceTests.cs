using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Context;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SessionAttachmentFileReferenceTests
{
    [Fact]
    public void FileIdsReuseStrictCanonicalAdmissionWithoutRepair()
    {
        foreach (var id in new[] { "", " ", Guid.Empty.ToString("D"), Guid.NewGuid().ToString("N"),
            "{aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa}", "(aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa)",
            "AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA", " aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa ", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\n",
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaａ", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa\u200b" })
        {
            ((Action)(() => _ = new SessionAttachmentQuery(exactFileSourceId: id))).Should().Throw<InvalidDataException>();
            ((Action)(() => _ = new SessionAttachmentQuery(exactFileRevisionId: id))).Should().Throw<InvalidDataException>();
            ((Action)(() => _ = new SessionAttachmentQuery(exactFileItemId: id))).Should().Throw<InvalidDataException>();
        }
    }

    [Fact]
    public void IndependentFileIdsAreAndPredicatesOnlyOverCertifiedAvailableCopies()
    {
        var row = SessionAttachmentSummaryTests.Summary();
        foreach (var state in Enum.GetValues<AttachmentSessionState>())
        foreach (var copy in Enum.GetValues<AttachmentCopyStatus>())
        foreach (var source in new string?[] { null, row.File.SourceId.ToString("D"), Guid.NewGuid().ToString("D") })
        foreach (var revision in new string?[] { null, row.File.RevisionId.ToString("D"), Guid.NewGuid().ToString("D") })
        foreach (var item in new string?[] { null, row.File.ItemId.ToString("D"), Guid.NewGuid().ToString("D") })
        foreach (var includeRemoved in new[] { false, true })
        {
            if (state == AttachmentSessionState.Removed && copy != AttachmentCopyStatus.Unavailable) { continue; }
            var candidate = row with { SessionState = state, CopyStatus = copy,
                StoredBytes = copy == AttachmentCopyStatus.Unavailable ? 0 : row.StoredBytes };
            var query = new SessionAttachmentQuery(includeRemoved, exactFileSourceId: source,
                exactFileRevisionId: revision, exactFileItemId: item);
            var expected = (includeRemoved || copy != AttachmentCopyStatus.Unavailable)
                && (!query.HasFileReferencePredicates || copy == AttachmentCopyStatus.Confirmed
                    && (source is null || string.Equals(source, row.File.SourceId.ToString("D"), StringComparison.Ordinal))
                    && (revision is null || string.Equals(revision, row.File.RevisionId.ToString("D"), StringComparison.Ordinal))
                    && (item is null || string.Equals(item, row.File.ItemId.ToString("D"), StringComparison.Ordinal)));
            query.Matches(candidate).Should().Be(expected);
        }
    }

    [Fact]
    public void FileIdsComposeWithEveryExistingPredicateWithoutChangingTheirMeaning()
    {
        var row = SessionAttachmentSummaryTests.Summary();
        foreach (var includeRemoved in new[] { false, true })
        foreach (var exact in new[] { false, true })
        foreach (var lifecycle in Enum.GetValues<SessionAttachmentFilter>())
        foreach (var literal in new string?[] { null, "text/", "TEXT/" })
        foreach (var media in new string?[] { null, LocalFileMediaType.Markdown, LocalFileMediaType.PlainText })
        foreach (var status in new AttachmentCopyStatus?[] { null, AttachmentCopyStatus.Confirmed, AttachmentCopyStatus.Held, AttachmentCopyStatus.Unavailable })
        foreach (var bound in new long?[] { null, row.StoredBytes, row.StoredBytes + 1 })
        {
            SessionAttachmentQuery Query(bool fileIds) => new(includeRemoved, exact ? row.Session.Value.ToString("D") : null,
                lifecycle, literal, media, status, bound, row.StoredBytes + 1, row.File.Digest,
                row.ObservationSource.ToString("D"), row.Profile.Value.ToString("D"), row.CapturedAt, row.CapturedAt,
                row.SessionGeneration.Value, row.SessionGeneration.Value, row.CapturedGeneration.Value, row.CapturedGeneration.Value,
                row.StorageRevision.Value, row.StorageRevision.Value, fileIds ? row.File.SourceId.ToString("D") : null,
                fileIds ? row.File.RevisionId.ToString("D") : null, fileIds ? row.File.ItemId.ToString("D") : null);
            Query(true).Matches(row).Should().Be(Query(false).Matches(row));
        }
        var matching = new SessionAttachmentQuery(exactFileSourceId: row.File.SourceId.ToString("D"),
            exactFileRevisionId: row.File.RevisionId.ToString("D"), exactFileItemId: row.File.ItemId.ToString("D"));
        foreach (var mismatch in new[] {
            new SessionAttachmentQuery(exactDigest: new string('b', 64), exactFileSourceId: row.File.SourceId.ToString("D")),
            new(exactObservationSource: Guid.NewGuid().ToString("D"), exactFileRevisionId: row.File.RevisionId.ToString("D")),
            new(exactDeviceProfile: Guid.NewGuid().ToString("D"), exactFileItemId: row.File.ItemId.ToString("D")),
            new(capturedFromUtc: row.CapturedAt.AddTicks(1), exactFileSourceId: row.File.SourceId.ToString("D")),
            new(capturedUntilUtc: row.CapturedAt.AddTicks(-1), exactFileSourceId: row.File.SourceId.ToString("D")),
            new(minimumSessionGeneration: row.SessionGeneration.Value + 1, exactFileSourceId: row.File.SourceId.ToString("D")),
            new(minimumCapturedGeneration: row.CapturedGeneration.Value + 1, exactFileSourceId: row.File.SourceId.ToString("D")),
            new(minimumStorageRevision: row.StorageRevision.Value + 1, exactFileSourceId: row.File.SourceId.ToString("D")) })
        { matching.Matches(row).Should().BeTrue(); mismatch.Matches(row).Should().BeFalse(); }
    }

    [Fact]
    public void NonmatchingFileIdsNeverHideInvalidCompleteSummaries()
    {
        var row = SessionAttachmentSummaryTests.Summary();
        var query = new SessionAttachmentQuery(exactFileSourceId: Guid.NewGuid().ToString("D"));
        foreach (var invalid in new[] { row with { ObservationSource = Guid.Empty }, row with { Profile = default },
            row with { Session = default }, row with { SessionGeneration = default }, row with { CapturedGeneration = new(3) },
            row with { StorageRevision = default }, row with { SessionState = (AttachmentSessionState)99 },
            row with { CopyStatus = (AttachmentCopyStatus)99 }, row with { File = null! },
            row with { File = row.File with { SourceId = Guid.Empty } },
            row with { File = row.File with { RevisionId = Guid.Empty } },
            row with { File = row.File with { ItemId = Guid.Empty } },
            row with { File = row.File with { Digest = new string('A', 64) } },
            row with { StoredBytes = -1 }, row with { MediaType = "invalid" },
            row with { CapturedAt = row.CapturedAt.ToOffset(TimeSpan.FromHours(1)) },
            row with { CopyStatus = AttachmentCopyStatus.Unavailable },
            row with { SessionState = AttachmentSessionState.Removed } })
        { query.Invoking(value => value.Matches(invalid)).Should().Throw<InvalidDataException>(); }
    }

    [Fact]
    public void PrivateIdentityAndCompleteEmptyPagesIncludeEveryFileIdWithoutSerializingQueries()
    {
        var row = SessionAttachmentSummaryTests.Summary();
        var baseline = new SessionAttachmentQuery(true);
        foreach (var query in new[] {
            new SessionAttachmentQuery(true, exactFileSourceId: row.File.SourceId.ToString("D")),
            new(true, exactFileRevisionId: row.File.RevisionId.ToString("D")),
            new(true, exactFileItemId: row.File.ItemId.ToString("D")) })
        {
            query.Should().NotBe(baseline);
            JsonSerializer.Serialize(query).Should().Be(JsonSerializer.Serialize(baseline));
            var page = new SessionAttachmentPage(query, [], null, DateTimeOffset.UnixEpoch, 1, new string('b', 64), 1, false);
            page.Validate();
            (page with { Records = [row with { CopyStatus = AttachmentCopyStatus.Unavailable, StoredBytes = 0 }] })
                .Invoking(value => value.Validate()).Should().Throw<InvalidDataException>();
        }
    }
}
