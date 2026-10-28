namespace Kora.Core.Skills;

/// <summary>An observed effect-boundary outcome, never proof of a later physical power state.</summary>
public enum FixedSessionEffectOutcome
{
    /// <summary>A possible effect or authority cleanup could not be confirmed.</summary>
    Unconfirmed,
    /// <summary>Windows accepted the request; physical completion has not been observed.</summary>
    RequestAccepted,
    /// <summary>The required current permission or interactive session is unavailable.</summary>
    Denied,
    /// <summary>Windows rejected the request before reporting acceptance.</summary>
    Failed,
}