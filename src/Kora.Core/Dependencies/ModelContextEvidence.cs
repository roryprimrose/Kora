using Kora.Core.Hosting;

namespace Kora.Core.Dependencies;

public sealed record ModelContextEvidence(
    HostId<EvidenceIdentity> Id, HostRequest Request, HostRevision Revision,
    string Content, ModelEvidenceDisclosure Disclosure);
