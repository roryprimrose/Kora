using Kora.Core.Skills;

namespace Kora.Windows.Skills;

/// <summary>The Windows effect boundary; only an admitted host invocation may call it.</summary>
internal interface IWindowsFixedSessionPlatform
{
    /// <summary>Checks the effective interactive session immediately before requesting an effect.</summary>
    bool IsCurrentInteractiveSession();

    /// <summary>Requests a session lock without changing token authority.</summary>
    FixedSessionEffectResult Lock();

    /// <summary>Duplicates the current effective identity, never an elevated or different account.</summary>
    IWindowsShutdownAuthority DuplicateShutdownAuthority();
}