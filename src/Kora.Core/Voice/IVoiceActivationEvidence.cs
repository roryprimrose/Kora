namespace Kora.Core.Voice;

/// <summary>
/// Carries an ephemeral activation observation, not permission or authentication.
/// Only the owning host can recognize and consume its own evidence for one utterance.
/// Implementing this interface or matching its generation confers no authority.
/// </summary>
public interface IVoiceActivationEvidence
{
    /// <summary>Gets the observed capture generation; this value alone is not activation proof.</summary>
    long Generation { get; }
}
