using Kora.Core.Diagnostics;

namespace Kora;

internal sealed record SessionEvidenceDetail(EvidenceSource Source, EvidencePage Page)
{
    internal Guid Id { get; } = Guid.NewGuid();
    public string Label => $"{(Source == EvidenceSource.AuthorityAudit ? "Typed security audit" : "Diagnostic log")}: "
        + $"{Page.Status}, {Page.Records.Count} records. HOST observation, not an assistant answer or authorization.";
}
