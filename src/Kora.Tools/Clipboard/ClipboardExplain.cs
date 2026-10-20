using Kora.Core.Context;

namespace Kora.Tools.Clipboard;

public sealed class ClipboardExplain(IClipboardExplanationWorkflow workflow)
{
    public Task<ClipboardExplanationResult> ExecuteAsync(Guid? reviewedSnapshotId,
        Func<bool> eligible, CancellationToken token) =>
        workflow.ExplainAsync(reviewedSnapshotId, eligible, token);
}