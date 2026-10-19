namespace Kora.Core.Diagnostics;

/// <summary>Commits bounded sets of its own admitted ordinary snapshots atomically.</summary>
public interface IBatchedEvidenceSink : IDeferredEvidenceSink
{
    const int MaximumBatchSize = 32;

    IPreparedEvidenceWrite PrepareDiagnosticWrite(DiagnosticEnvelope envelope);
    IPreparedEvidenceWrite PrepareActivityWrite(CompletedActivityEnvelope envelope);

    /// <summary>
    /// Validates storage integrity at the transaction boundary and durably commits every write,
    /// or fails the batch receipt. Pre-commit failures roll back every member; post-commit
    /// verification failures remain fatal. Rejects foreign snapshots; never batches typed audits.
    /// </summary>
    void WriteBatch(IReadOnlyList<IPreparedEvidenceWrite> writes);
}
