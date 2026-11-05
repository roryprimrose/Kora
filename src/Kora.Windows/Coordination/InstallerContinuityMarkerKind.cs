namespace Kora.Windows.Coordination;

/// <summary>Identifies the existing coordinator paths reserved by maintenance.</summary>
internal enum InstallerContinuityMarkerKind
{
    /// <summary>The assistant dirty marker, also consumed by Release startup.</summary>
    Owner,
    /// <summary>An intentionally non-Debug record that prevents automatic Debug recovery.</summary>
    DebugRecoveryGuard,
}
