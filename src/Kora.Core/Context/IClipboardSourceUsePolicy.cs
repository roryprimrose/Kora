namespace Kora.Core.Context;

/// <summary>
/// Owns clipboard secret/sensitive-content admission before context construction.
/// Explicit review and conversation credential heuristics are not this policy.
/// </summary>
public interface IClipboardSourceUsePolicy
{
    /// <summary>Evaluates the complete exact snapshot for one local-only use, without persistence or egress.</summary>
    Task<ClipboardSourceUseOutcome> EvaluateAsync(ClipboardSnapshot snapshot, CancellationToken token);
}
