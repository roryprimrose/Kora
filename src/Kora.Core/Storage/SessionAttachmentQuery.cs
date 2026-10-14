using System.Text.Json.Serialization;

namespace Kora.Core.Storage;

/// <summary>A query over retained slots, optionally including content-free removal provenance.</summary>
public sealed record SessionAttachmentQuery
{
    public SessionAttachmentQuery(bool includeRemoved = false, string? exactSessionId = null,
        SessionAttachmentFilter filter = SessionAttachmentFilter.All)
    {
        if (!Enum.IsDefined(filter)) { throw new InvalidDataException("Unknown attachment session lifecycle filter."); }
        Filter = filter;
        IncludeRemoved = includeRemoved;
        ExactSessionId = exactSessionId;
        ExactId = exactSessionId is null ? null
            : new SessionListSearch(SessionListSearchKind.ExactId, SessionListFilter.All, exactSessionId).ExactId;
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
    [JsonIgnore]
    public Guid? ExactId { get; }

    public bool Matches(SessionAttachmentSummary row) =>
        (IncludeRemoved || row.CopyStatus != AttachmentCopyStatus.Unavailable)
        && (ExactId is not { } exact || row.Session.Value == exact)
        && (Filter switch
        {
            SessionAttachmentFilter.All => true,
            SessionAttachmentFilter.Active => row.SessionState == AttachmentSessionState.Active,
            SessionAttachmentFilter.Done => row.SessionState == AttachmentSessionState.Done,
            SessionAttachmentFilter.Removed => row.SessionState == AttachmentSessionState.Removed,
            _ => throw new InvalidDataException("Unknown attachment session lifecycle filter.")
        });
}
