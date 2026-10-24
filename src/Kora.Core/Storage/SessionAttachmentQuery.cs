using System.Text.Json.Serialization;

using Kora.Core.Context;

namespace Kora.Core.Storage;

/// <summary>A bounded lifecycle, type and size query over retained attachment slots.</summary>
public sealed record SessionAttachmentQuery
{
    [JsonConstructor]
    public SessionAttachmentQuery(bool includeRemoved = false, string? exactSessionId = null,
        SessionAttachmentFilter filter = SessionAttachmentFilter.All, string? exactMediaType = null,
        AttachmentCopyStatus? copyStatus = null, long? minimumStoredBytes = null, long? maximumStoredBytes = null)
    {
        IncludeRemoved = includeRemoved;
        ExactSessionId = exactSessionId;
        ExactId = exactSessionId is null ? null
            : new SessionListSearch(SessionListSearchKind.ExactId, SessionListFilter.All, exactSessionId).ExactId;
        Filter = filter;
        ExactMediaType = exactMediaType;
        CopyStatus = copyStatus;
        MinimumStoredBytes = minimumStoredBytes;
        MaximumStoredBytes = maximumStoredBytes;
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
    }

    public bool Matches(SessionAttachmentSummary row)
    {
        Validate();
        row.Validate();
        return (IncludeRemoved || row.CopyStatus != AttachmentCopyStatus.Unavailable)
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
                || Filter == SessionAttachmentFilter.Removed && row.SessionState == AttachmentSessionState.Removed);
    }
}
