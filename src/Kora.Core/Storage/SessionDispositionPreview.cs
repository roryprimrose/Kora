namespace Kora.Core.Storage;

public sealed record SessionDispositionPreview(
    Guid ConfirmationId, SessionWorkspaceEntry Session, string StoreRevision,
    long Questions, long ScopedGrants, long Observations, long Waits)
{
    public SessionStoppedWorkReview? StoppedWork { get; init; }
    public long Attachments { get; init; }
    public long ProviderProfiles { get; init; }
    public long ConversationMessages { get; init; }
    public long SavedConversationDrafts { get; init; }
    public const string Scope =
        "Exact session disposition: removes this exact session's live name, questions (including drafts/answers), "
        + "host observations, pre-dispatch wait bindings and scoped grants. Retains a non-reusable identity tombstone, "
        + "task/event, content-free queue identity/timing/state and content-minimising authority audit provenance, independent diagnostics and Perpetual grants. "
        + "History content and session-owned reviewed-memory bodies are redacted atomically; exact event IDs, sequence, revisions and digests remain readable. Independent device-profile memories remain retained. "
        + "Exact session provider overrides are inventoried and removed from the private SQLite authority and committed journal, never promoted to app preferences. "
        + "Exact session file attachments, when present, are inventoried, revoked and securely removed from owned SQLite cells/free pages and the committed rollback journal; uninventoried copies hold completion. "
        + "Original USER conversation bodies, separately retained ASSISTANT answers and explicitly saved drafts are inventoried and removed from owned SQLite cells/free pages and committed journals; "
        + "All addressed name, question/draft/answer, observation, history and session-memory content is also removed from owned SQLite cells/free pages, "
        + "and the committed rollback journal is verified empty before completion, even without attachments or conversation records. "
        + "Unconfirmed post-commit cleanup holds completion; inspect durable authority and recovery, never infer rollback or retry automatically. "
        + "General managed-artifact or backup deletion is not delivered. Other databases and their journals/free pages, "
        + "inert legacy storage, user exports and provider copies are not erased. This is not forensic deletion.";
}
