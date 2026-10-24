namespace Kora.Application.Voice;

/// <summary>Observed result of a run-only listening stop, not a global microphone mute.</summary>
internal enum ListeningStopOutcome
{
    /// <summary>Input is disabled and native capture release is confirmed.</summary>
    Disabled,

    /// <summary>Input was already disabled and native capture release is confirmed.</summary>
    AlreadyDisabled,

    /// <summary>Input is disabled but native capture release could not be confirmed.</summary>
    ReleaseUnconfirmed,
}
