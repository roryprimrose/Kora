namespace Kora.Core.Context;

/// <summary>
/// Owns permission-grant admission before local clipboard context construction.
/// Content is the user's responsibility; this is not sensitivity classification.
/// </summary>
public interface IClipboardSourceUsePolicy
{
    /// <summary>Evaluates the complete exact snapshot for one local-only use, without persistence or egress.</summary>
    Task<ClipboardSourceUseOutcome> EvaluateAsync(ClipboardSnapshot snapshot, Func<bool> current, CancellationToken token);

    /// <summary>Revalidates the consumed permission at the actual model boundary.</summary>
    Task ValidateAsync(ClipboardSnapshot snapshot, CancellationToken token);
}
