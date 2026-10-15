using Kora.Core.Context;
using Kora.Core.Hosting;
using Kora.Core.Memory;

namespace Kora.Core.Storage;

/// <summary>Content-free exact retained slot metadata; never source consent or request authority.</summary>
public sealed record SessionAttachmentSummary(
    Guid ObservationSource, HostId<DeviceProfileIdentity> Profile, HostId<SessionIdentity> Session,
    HostRevision SessionGeneration, HostRevision CapturedGeneration, HostRevision StorageRevision,
    AttachmentSessionState SessionState, [property: System.Text.Json.Serialization.JsonIgnore] LocalFileReference File, long StoredBytes, string MediaType,
    DateTimeOffset CapturedAt, AttachmentCopyStatus CopyStatus)
{
    /// <summary>Removal provenance never serializes a prior digest, even before candidate validation.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("File")]
    public SessionAttachmentFileMetadata FileMetadata => new(File.SourceId, File.RevisionId, File.ItemId, RetainedDigest);

    [System.Text.Json.Serialization.JsonIgnore]
    public string? RetainedDigest => CopyStatus is AttachmentCopyStatus.Confirmed or AttachmentCopyStatus.Held
        && SessionState is AttachmentSessionState.Active or AttachmentSessionState.Done ? File.Digest : null;

    /// <summary>Unavailable slots carry a zero sentinel, not an original-size observation.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public long? RetainedStoredBytes => CopyStatus == AttachmentCopyStatus.Unavailable ? null : StoredBytes;

    public void Validate()
    {
        File?.Validate();
        if (ObservationSource == Guid.Empty || Profile.Value == Guid.Empty || Session.Value == Guid.Empty
            || SessionGeneration.Value <= 0 || CapturedGeneration.Value <= 0
            || CapturedGeneration.Value > SessionGeneration.Value || StorageRevision.Value <= 0
            || !Enum.IsDefined(SessionState) || !Enum.IsDefined(CopyStatus)
            || File is null || StoredBytes is < 0 or > LocalFilePolicy.MaximumBytes
            || !LocalFileMediaType.IsCanonical(MediaType) || CapturedAt.Offset != TimeSpan.Zero
            || CopyStatus == AttachmentCopyStatus.Unavailable && StoredBytes != 0
            || SessionState == AttachmentSessionState.Removed && CopyStatus != AttachmentCopyStatus.Unavailable)
        {
            throw new InvalidDataException("The attachment summary has an invalid exact identity, lifecycle or metadata shape.");
        }
    }

    public void RequireAttachment(SessionFileAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        Validate();
        if (CopyStatus != AttachmentCopyStatus.Confirmed || attachment.Session != Session
            || attachment.Generation != CapturedGeneration || attachment.StorageRevision != StorageRevision
            || attachment.File.Reference != File || attachment.File.AdmittedAt != CapturedAt
            || attachment.File.Review.Metadata.ByteLength != StoredBytes)
        {
            throw new InvalidOperationException("The selected exact retained attachment changed or is unavailable. Refresh the catalogue.");
        }
    }
}
