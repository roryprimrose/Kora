namespace Kora.Core.Context;

/// <summary>One explicit native review of one exact current immutable clipboard snapshot.</summary>
public interface IClipboardExplanationWorkflow
{
    /// <summary>Requests review, or admits the exact native-reviewed snapshot ID for local-only explanation.</summary>
    Task<ClipboardExplanationResult> ExplainAsync(Guid? reviewedSnapshotId, Func<bool> eligible, CancellationToken token);
}