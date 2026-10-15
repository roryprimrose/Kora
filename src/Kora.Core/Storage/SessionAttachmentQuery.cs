using System.Text.Json.Serialization;

using Kora.Core.Context;

namespace Kora.Core.Storage;

/// <summary>A query over retained slots, optionally including content-free removal provenance.</summary>
public sealed record SessionAttachmentQuery
{
    public SessionAttachmentQuery(bool includeRemoved = false, string? exactSessionId = null,
        SessionAttachmentFilter filter = SessionAttachmentFilter.All, string? metadataText = null,
        string? exactMediaType = null, AttachmentCopyStatus? copyStatus = null,
        long? minimumStoredBytes = null, long? maximumStoredBytes = null, string? exactDigest = null)
    {
        if (!Enum.IsDefined(filter)) { throw new InvalidDataException("Unknown attachment session lifecycle filter."); }
        Filter = filter;
        IncludeRemoved = includeRemoved;
        ExactSessionId = exactSessionId;
        ExactId = exactSessionId is null ? null
            : new SessionListSearch(SessionListSearchKind.ExactId, SessionListFilter.All, exactSessionId).ExactId;
        MetadataText = metadataText;
        ExactMediaType = exactMediaType;
        CopyStatus = copyStatus;
        MinimumStoredBytes = minimumStoredBytes;
        MaximumStoredBytes = maximumStoredBytes;
        ExactDigest = exactDigest;
        Validate();
    }

    [JsonRequired]
    [JsonInclude]
    public bool IncludeRemoved { get; private init; }
    [JsonRequired]
    [JsonInclude]
    public string? ExactSessionId { get; private init; }
    [JsonRequired]
    [JsonInclude]
    public SessionAttachmentFilter Filter { get; private init; }
    [JsonRequired]
    [JsonInclude]
    public string? ExactMediaType { get; private init; }
    [JsonRequired]
    [JsonInclude]
    public AttachmentCopyStatus? CopyStatus { get; private init; }
    [JsonRequired]
    [JsonInclude]
    public long? MinimumStoredBytes { get; private init; }
    [JsonRequired]
    [JsonInclude]
    public long? MaximumStoredBytes { get; private init; }
    [JsonIgnore]
    public Guid? ExactId { get; }
    [JsonIgnore]
    public string? MetadataText { get; }
    [JsonIgnore]
    public string? ExactDigest { get; }

    public void Validate()
    {
        if (!Enum.IsDefined(Filter)) { throw new InvalidDataException("Unknown attachment session lifecycle filter."); }
        if (CopyStatus is { } copy && !Enum.IsDefined(copy))
        { throw new InvalidDataException("Unknown attachment copy-status filter."); }
        if (MinimumStoredBytes is < 0 or > LocalFilePolicy.MaximumBytes
            || MaximumStoredBytes is < 0 or > LocalFilePolicy.MaximumBytes
            || MinimumStoredBytes > MaximumStoredBytes)
        { throw new InvalidDataException("Stored byte bounds must be within 0 through the local file maximum, inclusive, with minimum <= maximum."); }
        if (ExactMediaType is not null) { LocalFileMediaType.Validate(ExactMediaType); }
        if (ExactDigest is not null) { LocalFileDigest.Validate(ExactDigest); }
        if (MetadataText is null) { return; }
        LiteralMetadataQuery.Validate(MetadataText);
        if (MetadataText.Length == 0) { throw new InvalidDataException("A literal attachment metadata query must be nonempty."); }
    }

    public bool Matches(SessionAttachmentSummary row)
    {
        Validate();
        row.Validate();
        return
        (IncludeRemoved || row.CopyStatus != AttachmentCopyStatus.Unavailable)
        && (ExactDigest is null || row.CopyStatus == AttachmentCopyStatus.Confirmed
            && string.Equals(row.File.Digest, ExactDigest, StringComparison.Ordinal))
        && (CopyStatus is null || row.CopyStatus == CopyStatus)
        && (MinimumStoredBytes is null && MaximumStoredBytes is null
            || row.CopyStatus != AttachmentCopyStatus.Unavailable
                && (MinimumStoredBytes is null || row.StoredBytes >= MinimumStoredBytes)
                && (MaximumStoredBytes is null || row.StoredBytes <= MaximumStoredBytes))
        && (ExactMediaType is null || row.CopyStatus != AttachmentCopyStatus.Unavailable
            && string.Equals(row.MediaType, ExactMediaType, StringComparison.Ordinal))
        && (ExactId is not { } exact || row.Session.Value == exact)
        && (Filter == SessionAttachmentFilter.All
            || Filter == SessionAttachmentFilter.Active && row.SessionState == AttachmentSessionState.Active
            || Filter == SessionAttachmentFilter.Done && row.SessionState == AttachmentSessionState.Done
            || Filter == SessionAttachmentFilter.Removed && row.SessionState == AttachmentSessionState.Removed)
        && (MetadataText is null || row.CopyStatus != AttachmentCopyStatus.Unavailable
            && (row.Session.Value.ToString("D").Contains(MetadataText, StringComparison.Ordinal)
                || row.File.SourceId.ToString("D").Contains(MetadataText, StringComparison.Ordinal)
                || row.File.RevisionId.ToString("D").Contains(MetadataText, StringComparison.Ordinal)
                || row.File.ItemId.ToString("D").Contains(MetadataText, StringComparison.Ordinal)
                || row.File.Digest.Contains(MetadataText, StringComparison.Ordinal)
                || row.MediaType.Contains(MetadataText, StringComparison.Ordinal)));
    }
}
