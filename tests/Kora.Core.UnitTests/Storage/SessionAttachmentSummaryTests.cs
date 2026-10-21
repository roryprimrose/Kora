using System.Text;

using AwesomeAssertions;

using Kora.Core.Context;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Core.UnitTests.Storage;

public sealed class SessionAttachmentSummaryTests
{
    [Fact]
    public void RetainedMetadataIsRedactedBeforeValidationForEveryLifecycleAndCopyState()
    {
        foreach (var state in Enum.GetValues<AttachmentSessionState>())
        foreach (var copy in Enum.GetValues<AttachmentCopyStatus>())
        {
            var row = Summary() with { SessionState = state, CopyStatus = copy };
            var retained = state is AttachmentSessionState.Active or AttachmentSessionState.Done
                && copy is AttachmentCopyStatus.Confirmed or AttachmentCopyStatus.Held;
            row.RetainedObservationSource.Should().Be(retained ? row.ObservationSource : null);
            row.RetainedProfile.Should().Be(retained ? row.Profile : null);
            row.RetainedCapturedAt.Should().Be(retained ? row.CapturedAt : null);
            row.RetainedSessionGeneration.Should().Be(retained ? row.SessionGeneration : null);
            row.RetainedCapturedGeneration.Should().Be(retained ? row.CapturedGeneration : null);
            row.RetainedStorageRevision.Should().Be(retained ? row.StorageRevision : null);
            row.RetainedDigest.Should().Be(retained ? row.File.Digest : null);
        }
    }
    [Theory]
    [InlineData("source")]
    [InlineData("profile")]
    [InlineData("session")]
    [InlineData("generation")]
    [InlineData("captured-generation")]
    [InlineData("future-generation")]
    [InlineData("storage")]
    [InlineData("lifecycle")]
    [InlineData("copy")]
    [InlineData("file")]
    [InlineData("file-source")]
    [InlineData("file-revision")]
    [InlineData("file-item")]
    [InlineData("digest-null")]
    [InlineData("digest-length")]
    [InlineData("digest-character")]
    [InlineData("digest-zero")]
    [InlineData("bytes-negative")]
    [InlineData("bytes-large")]
    [InlineData("type")]
    [InlineData("utc")]
    [InlineData("removed-bytes")]
    [InlineData("removed-live")]
    public void ValidateRejectsMalformedContentFreeShape(string boundary)
    {
        var valid = Summary();
        var value = boundary switch
        {
            "source" => valid with { ObservationSource = Guid.Empty },
            "profile" => valid with { Profile = default },
            "session" => valid with { Session = default },
            "generation" => valid with { SessionGeneration = default },
            "captured-generation" => valid with { CapturedGeneration = default },
            "future-generation" => valid with { CapturedGeneration = new(3) },
            "storage" => valid with { StorageRevision = default },
            "lifecycle" => valid with { SessionState = (AttachmentSessionState)99 },
            "copy" => valid with { CopyStatus = (AttachmentCopyStatus)99 },
            "file" => valid with { File = null! },
            "file-source" => valid with { File = valid.File with { SourceId = Guid.Empty } },
            "file-revision" => valid with { File = valid.File with { RevisionId = Guid.Empty } },
            "file-item" => valid with { File = valid.File with { ItemId = Guid.Empty } },
            "digest-null" => valid with { File = valid.File with { Digest = null! } },
            "digest-length" => valid with { File = valid.File with { Digest = "a" } },
            "digest-character" => valid with { File = valid.File with { Digest = new string('A', 64) } },
            "digest-zero" => valid with { File = valid.File with { Digest = new string('0', 64) } },
            "bytes-negative" => valid with { StoredBytes = -1 },
            "bytes-large" => valid with { StoredBytes = LocalFilePolicy.MaximumBytes + 1 },
            "type" => valid with { MediaType = "application/octet-stream" },
            "utc" => valid with { CapturedAt = DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(1)) },
            "removed-bytes" => valid with { CopyStatus = AttachmentCopyStatus.Unavailable },
            _ => valid with { SessionState = AttachmentSessionState.Removed },
        };
        value.Invoking(item => item.Validate()).Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(AttachmentSessionState.Active, AttachmentCopyStatus.Confirmed, "text/plain", 0)]
    [InlineData(AttachmentSessionState.Done, AttachmentCopyStatus.Held, "text/markdown", LocalFilePolicy.MaximumBytes)]
    [InlineData(AttachmentSessionState.Removed, AttachmentCopyStatus.Unavailable, "text/plain", 0)]
    public void ValidateAcceptsStoredLifecycleAndCertificationWithoutClaimingBodyVerification(
        AttachmentSessionState state, AttachmentCopyStatus copy, string type, long bytes)
    {
        (Summary() with { SessionState = state, CopyStatus = copy, MediaType = type, StoredBytes = bytes })
            .Invoking(item => item.Validate()).Should().NotThrow();
    }

    [Theory]
    [InlineData("copy")]
    [InlineData("session")]
    [InlineData("generation")]
    [InlineData("storage")]
    [InlineData("file")]
    [InlineData("captured")]
    [InlineData("bytes")]
    public void RequireAttachmentRejectsNewerOrDifferentExactBody(string boundary)
    {
        var attachment = Attachment();
        var summary = Summary(attachment);
        summary.RequireAttachment(attachment);
        var stale = boundary switch
        {
            "copy" => summary with { CopyStatus = AttachmentCopyStatus.Held },
            "session" => summary with { Session = new(Guid.NewGuid()) },
            "generation" => summary with { CapturedGeneration = new(2) },
            "storage" => summary with { StorageRevision = new(2) },
            "file" => summary with { File = summary.File with { SourceId = Guid.NewGuid() } },
            "captured" => summary with { CapturedAt = summary.CapturedAt.AddSeconds(1) },
            _ => summary with { StoredBytes = 1 },
        };
        stale.Invoking(item => item.RequireAttachment(attachment)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RequireAttachmentRejectsMissingBody()
    {
        Summary().Invoking(item => item.RequireAttachment(null!)).Should().Throw<ArgumentNullException>();
    }

    internal static SessionFileAttachment Attachment()
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        byte[] bytes = Encoding.UTF8.GetBytes("private content never included in a summary");
        return new(request.SessionId, new(1), new(1), new(
            new(Guid.NewGuid(), Guid.NewGuid(), request, new(@"C:\Synthetic\source.md", "exact", bytes.Length, DateTimeOffset.UnixEpoch)),
            bytes, DateTimeOffset.UnixEpoch));
    }

    internal static SessionAttachmentSummary Summary(SessionFileAttachment? attachment = null)
    {
        var exact = attachment ?? Attachment();
        return new(Guid.NewGuid(), new(Guid.NewGuid()), exact.Session, new(2), exact.Generation,
            exact.StorageRevision, AttachmentSessionState.Done, exact.File.Reference,
            exact.File.Review.Metadata.ByteLength, "text/markdown", exact.File.AdmittedAt, AttachmentCopyStatus.Confirmed);
    }
}
