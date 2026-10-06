namespace Kora.Windows.Storage;

internal sealed record ArtifactReconciliation(IReadOnlyList<ArtifactRecoveryIssue> Issues, int ExaminedFiles, long ExaminedBytes);
