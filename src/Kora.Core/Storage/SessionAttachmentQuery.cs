using System.Text.Json.Serialization;

namespace Kora.Core.Storage;

/// <summary>A query over retained slots, optionally including content-free removal provenance.</summary>
public sealed record SessionAttachmentQuery
{
    public SessionAttachmentQuery(bool includeRemoved = false, string? exactSessionId = null)
    {
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
    [JsonIgnore]
    public Guid? ExactId { get; }

    public bool Matches(SessionAttachmentSummary row) =>
        (IncludeRemoved || row.CopyStatus != AttachmentCopyStatus.Unavailable)
        && (ExactId is not { } exact || row.Session.Value == exact);
}
