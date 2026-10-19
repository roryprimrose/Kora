namespace Kora.Windows.Storage;

internal sealed record EvidenceSegmentReadView(string Id, long AuditBase, WindowsSqliteEvidenceSink Sink);
