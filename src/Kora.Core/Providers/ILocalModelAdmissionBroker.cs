namespace Kora.Core.Providers;

/// <summary>Platform-owned exact subject and receipt boundary. The Application workflow owns separate user admission.</summary>
public interface ILocalModelAdmissionBroker
{
    /// <summary>Observes an independently qualified subject without installing, starting or executing a model.</summary>
    Task<LocalModelAdmissionCandidate?> Observe(LocalModelQualificationEvidence qualification, CancellationToken cancellationToken);

    /// <summary>Revalidates the exact previously issued observation; a substitute cannot satisfy it.</summary>
    Task<bool> IsCurrent(LocalModelQualificationEvidence qualification, LocalModelAdmissionCandidate expected, CancellationToken cancellationToken);

    /// <summary>Publishes a volatile receipt only after the host's current, audited review and durable choice are confirmed.</summary>
    void Admit(LocalModelQualificationEvidence qualification, LocalModelAdmissionCandidate expected, Func<bool> eligible, CancellationToken revoked);

    /// <summary>Revokes live receipts and releases volatile identity state, without affecting an external process.</summary>
    void Revoke();
}