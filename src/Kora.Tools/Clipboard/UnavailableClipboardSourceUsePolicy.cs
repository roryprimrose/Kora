using Kora.Core.Context;

namespace Kora.Tools.Clipboard;

/// <summary>No authoritative clipboard secret/sensitive classification rule is implemented.</summary>
public sealed class UnavailableClipboardSourceUsePolicy : IClipboardSourceUsePolicy
{
    public Task<ClipboardSourceUseOutcome> EvaluateAsync(ClipboardSnapshot snapshot, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        token.ThrowIfCancellationRequested();
        return Task.FromResult(ClipboardSourceUseOutcome.Unavailable);
    }
}
