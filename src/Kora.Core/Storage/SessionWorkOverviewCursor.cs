namespace Kora.Core.Storage;

/// <summary>A volatile host-, admission-, source- and filter-bound continuation after a consumed immutable ID.</summary>
public sealed record SessionWorkOverviewCursor(
    Guid Host, long AdmissionRevision, long SourceEpoch, Guid After, SessionListFilter Filter, bool NeedsAnswer);
