using System.Text.Json.Serialization;

using Kora.Core.Context;
using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>A query over retained slots, optionally including content-free removal provenance.</summary>
public sealed record SessionAttachmentQuery
{
    [JsonConstructor]
    private SessionAttachmentQuery(bool includeRemoved, string? exactSessionId, SessionAttachmentFilter filter,
        string? exactMediaType, AttachmentCopyStatus? copyStatus, long? minimumStoredBytes, long? maximumStoredBytes)
        : this(includeRemoved, exactSessionId, filter, null, exactMediaType, copyStatus,
            minimumStoredBytes, maximumStoredBytes, null)
    {
    }

    public SessionAttachmentQuery(bool includeRemoved = false, string? exactSessionId = null,
        SessionAttachmentFilter filter = SessionAttachmentFilter.All, string? metadataText = null,
        string? exactMediaType = null, AttachmentCopyStatus? copyStatus = null,
        long? minimumStoredBytes = null, long? maximumStoredBytes = null, string? exactDigest = null,
        string? exactObservationSource = null, string? exactDeviceProfile = null,
        DateTimeOffset? capturedFromUtc = null, DateTimeOffset? capturedUntilUtc = null,
        long? minimumSessionGeneration = null, long? maximumSessionGeneration = null,
        long? minimumCapturedGeneration = null, long? maximumCapturedGeneration = null,
        long? minimumStorageRevision = null, long? maximumStorageRevision = null)
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
        ExactObservationSource = exactObservationSource is null ? null
            : new SessionListSearch(SessionListSearchKind.ExactId, SessionListFilter.All, exactObservationSource).ExactId;
        ExactDeviceProfile = exactDeviceProfile is null ? null
            : new SessionListSearch(SessionListSearchKind.ExactId, SessionListFilter.All, exactDeviceProfile).ExactId;
        CapturedFromUtc = capturedFromUtc;
        CapturedUntilUtc = capturedUntilUtc;
        MinimumSessionGeneration = minimumSessionGeneration;
        MaximumSessionGeneration = maximumSessionGeneration;
        MinimumCapturedGeneration = minimumCapturedGeneration;
        MaximumCapturedGeneration = maximumCapturedGeneration;
        MinimumStorageRevision = minimumStorageRevision;
        MaximumStorageRevision = maximumStorageRevision;
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
    [JsonIgnore]
    public Guid? ExactObservationSource { get; }
    [JsonIgnore]
    public Guid? ExactDeviceProfile { get; }
    [JsonIgnore]
    public DateTimeOffset? CapturedFromUtc { get; }
    [JsonIgnore]
    public DateTimeOffset? CapturedUntilUtc { get; }
    [JsonIgnore]
    public long? MinimumSessionGeneration { get; }
    [JsonIgnore]
    public long? MaximumSessionGeneration { get; }
    [JsonIgnore]
    public long? MinimumCapturedGeneration { get; }
    [JsonIgnore]
    public long? MaximumCapturedGeneration { get; }
    [JsonIgnore]
    public long? MinimumStorageRevision { get; }
    [JsonIgnore]
    public long? MaximumStorageRevision { get; }
    [JsonIgnore]
    public bool HasVersionPredicates => MinimumSessionGeneration is not null || MaximumSessionGeneration is not null
        || MinimumCapturedGeneration is not null || MaximumCapturedGeneration is not null
        || MinimumStorageRevision is not null || MaximumStorageRevision is not null;
    [JsonIgnore]
    public bool HasCorrelationPredicates => ExactObservationSource is not null || ExactDeviceProfile is not null
        || CapturedFromUtc is not null || CapturedUntilUtc is not null;

    public void Validate()
    {
        ValidateRange(MinimumSessionGeneration, MaximumSessionGeneration);
        ValidateRange(MinimumCapturedGeneration, MaximumCapturedGeneration);
        ValidateRange(MinimumStorageRevision, MaximumStorageRevision);
        if (!Enum.IsDefined(Filter)) { throw new InvalidDataException("Unknown attachment session lifecycle filter."); }
        if (CopyStatus is { } copy && !Enum.IsDefined(copy))
        { throw new InvalidDataException("Unknown attachment copy-status filter."); }
        if (MinimumStoredBytes is < 0 or > LocalFilePolicy.MaximumBytes
            || MaximumStoredBytes is < 0 or > LocalFilePolicy.MaximumBytes
            || MinimumStoredBytes > MaximumStoredBytes)
        { throw new InvalidDataException("Stored byte bounds must be within 0 through the local file maximum, inclusive, with minimum <= maximum."); }
        if (ExactMediaType is not null) { LocalFileMediaType.Validate(ExactMediaType); }
        if (ExactDigest is not null) { LocalFileDigest.Validate(ExactDigest); }
        if (CapturedFromUtc is { Offset: var fromOffset } && fromOffset != TimeSpan.Zero
            || CapturedUntilUtc is { Offset: var untilOffset } && untilOffset != TimeSpan.Zero
            || CapturedFromUtc > CapturedUntilUtc)
        { throw new InvalidDataException("Captured bounds must be canonical UTC with From <= Until. Empty means Any."); }
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
        && (!HasVersionPredicates || row.CopyStatus == AttachmentCopyStatus.Confirmed
            && MatchesRange(row.SessionGeneration, MinimumSessionGeneration, MaximumSessionGeneration)
            && MatchesRange(row.CapturedGeneration, MinimumCapturedGeneration, MaximumCapturedGeneration)
            && MatchesRange(row.StorageRevision, MinimumStorageRevision, MaximumStorageRevision))
        && (!HasCorrelationPredicates || row.CopyStatus == AttachmentCopyStatus.Confirmed
            && (ExactObservationSource is not { } source || row.ObservationSource == source)
            && (ExactDeviceProfile is not { } profile || row.Profile.Value == profile)
            && (CapturedFromUtc is not { } from || row.CapturedAt >= from)
            && (CapturedUntilUtc is not { } until || row.CapturedAt <= until))
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

    private static bool MatchesRange(HostRevision value, long? minimum, long? maximum) =>
        (minimum is null || value.Value >= minimum) && (maximum is null || value.Value <= maximum);

    private static void ValidateRange(long? minimum, long? maximum)
    {
        try
        {
            if (minimum is { } lower) { _ = new HostRevision(lower); }
            if (maximum is { } upper) { _ = new HostRevision(upper); }
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException("Generation/revision bounds must be positive decimal integers. Empty means Any.", exception);
        }
        if (minimum > maximum)
        { throw new InvalidDataException("Generation/revision range minimum must be <= maximum. Empty means Any."); }
    }
}
