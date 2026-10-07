namespace Kora.Windows.Storage;

public sealed record DiagnosticRetentionBatch(int Logs, int Spans, int Links, bool HasMore);
