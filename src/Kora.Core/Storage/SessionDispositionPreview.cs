namespace Kora.Core.Storage;

public sealed record SessionDispositionPreview(
    Guid ConfirmationId, SessionWorkspaceEntry Session, string StoreRevision,
    long Questions, long ScopedGrants, long Observations, long Waits)
{
    public long Attachments { get; init; }
    public const string Scope =
        "Logical disposition only: removes this exact session's live name, questions (including drafts/answers), "
        + "host observations, pre-dispatch wait bindings and scoped grants. Retains a non-reusable identity tombstone, "
        + "task/event, content-free queue identity/timing/state and content-minimising authority audit provenance, independent diagnostics and Perpetual grants. "
        + "History content and session-owned reviewed-memory bodies are redacted atomically; exact event IDs, sequence, revisions and digests remain readable. "
        + "Exact session file attachments, when present, are inventoried, revoked and securely removed from owned SQLite cells/free pages and the committed rollback journal; uninventoried copies hold completion. "
        + "No full conversation, general managed-artifact or backup deletion is delivered. Other SQLite journals/free pages, "
        + "inert legacy storage, user exports and provider copies are not erased. This is not forensic deletion.";
}
