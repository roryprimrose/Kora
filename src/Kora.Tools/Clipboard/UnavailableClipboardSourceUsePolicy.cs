using Kora.Core.Context;

namespace Kora.Tools.Clipboard;

/// <summary>Fails closed when clipboard permission admission is unavailable.</summary>
public sealed class UnavailableClipboardSourceUsePolicy : IClipboardSourceUsePolicy
{
    public Task<ClipboardSourceUseOutcome> EvaluateAsync(ClipboardSnapshot snapshot, Func<bool> current, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        token.ThrowIfCancellationRequested();
        return Task.FromResult(ClipboardSourceUseOutcome.Unavailable);
    }

    public Task ValidateAsync(ClipboardSnapshot snapshot, CancellationToken token) =>
        throw new InvalidOperationException("No clipboard permission was admitted.");
}
