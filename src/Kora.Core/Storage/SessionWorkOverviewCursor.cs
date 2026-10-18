using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>A volatile host-, admission-, source-, filter- and query-bound continuation after a consumed immutable ID.</summary>
public sealed record SessionWorkOverviewCursor(
    Guid Host, long AdmissionRevision, long SourceEpoch, Guid After, SessionListFilter Filter, bool NeedsAnswer,
    string? QueryDigest = null, SessionWorkOverviewAvailability? Availability = null,
    SessionWorkOverviewContent Content = SessionWorkOverviewContent.Any,
    SessionWorkOverviewRanges Ranges = default, HostTaskState? TaskState = null,
    SessionWorkOverviewTaskQuery TaskQuery = default);
