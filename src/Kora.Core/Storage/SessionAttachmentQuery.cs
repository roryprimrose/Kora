using System.Text.Json.Serialization;

using Kora.Core.Context;

namespace Kora.Core.Storage;

/// <summary>A query over retained slots, optionally including content-free removal provenance.</summary>
public sealed record SessionAttachmentQuery
{
    public SessionAttachmentQuery(bool includeRemoved = false, string? exactSessionId = null,
        SessionAttachmentFilter filter = SessionAttachmentFilter.All, string? metadataText = null,
        string? exactMediaType = null)
    {
        if (!Enum.IsDefined(filter)) { throw new InvalidDataException("Unknown attachment session lifecycle filter."); }
        Filter = filter;
        IncludeRemoved = includeRemoved;
        ExactSessionId = exactSessionId;
        ExactId = exactSessionId is null ? null
            : new SessionListSearch(SessionListSearchKind.ExactId, SessionListFilter.All, exactSessionId).ExactId;
        MetadataText = metadataText;
        ExactMediaType = exactMediaType;
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
    [JsonIgnore]
    public Guid? ExactId { get; }
    [JsonIgnore]
    public string? MetadataText { get; }

    public void Validate()
    {
        if (!Enum.IsDefined(Filter)) { throw new InvalidDataException("Unknown attachment session lifecycle filter."); }
        if (ExactMediaType is not null) { LocalFileMediaType.Validate(ExactMediaType); }
        if (MetadataText is null) { return; }
        LiteralMetadataQuery.Validate(MetadataText);
        if (MetadataText.Length == 0) { throw new InvalidDataException("A literal attachment metadata query must be nonempty."); }
    }

    public bool Matches(SessionAttachmentSummary row)
    {
        if (ExactMediaType is not null) { LocalFileMediaType.Validate(ExactMediaType); }
        return
        (IncludeRemoved || row.CopyStatus != AttachmentCopyStatus.Unavailable)
        && (ExactMediaType is null || row.CopyStatus != AttachmentCopyStatus.Unavailable
            && string.Equals(row.MediaType, ExactMediaType, StringComparison.Ordinal))
        && (ExactId is not { } exact || row.Session.Value == exact)
        && (Filter switch
        {
            SessionAttachmentFilter.All => true,
            SessionAttachmentFilter.Active => row.SessionState == AttachmentSessionState.Active,
            SessionAttachmentFilter.Done => row.SessionState == AttachmentSessionState.Done,
            SessionAttachmentFilter.Removed => row.SessionState == AttachmentSessionState.Removed,
            _ => throw new InvalidDataException("Unknown attachment session lifecycle filter.")
        })
        && (MetadataText is null || row.CopyStatus != AttachmentCopyStatus.Unavailable
            && (row.Session.Value.ToString("D").Contains(MetadataText, StringComparison.Ordinal)
                || row.File.SourceId.ToString("D").Contains(MetadataText, StringComparison.Ordinal)
                || row.File.RevisionId.ToString("D").Contains(MetadataText, StringComparison.Ordinal)
                || row.File.ItemId.ToString("D").Contains(MetadataText, StringComparison.Ordinal)
                || row.File.Digest.Contains(MetadataText, StringComparison.Ordinal)
                || row.MediaType.Contains(MetadataText, StringComparison.Ordinal)));
    }
}
