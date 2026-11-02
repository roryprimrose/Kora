using Kora.Core.Hosting;

namespace Kora.Core.Dependencies;

public sealed record ModelContextEvidence(
    HostId<EvidenceIdentity> Id, HostRequest Request, HostRevision Revision,
    string Content, ModelEvidenceDisclosure Disclosure)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public Conversation.SubmittedHistorySource? HistorySource { get; init; }
}
