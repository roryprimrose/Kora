namespace Kora.Core.Skills;

/// <summary>The closed set of computer effects supported by the fixed bundled packages.</summary>
public enum FixedSessionAction
{
    /// <summary>Request a lock of the current interactive Windows session.</summary>
    Lock,
    /// <summary>Request a graceful local computer shutdown without forcing applications closed.</summary>
    Shutdown,
    /// <summary>Request a graceful local computer restart without forcing applications closed.</summary>
    Restart,
}