namespace Kora.Core.Artifacts;

/// <summary>The observed durable boundary of a bounded rename.</summary>
public enum OwnedInstructionRenamePhase
{
    /// <summary>The original still exists and publication has not started.</summary>
    Prepared,
    /// <summary>The original is retained in its native recovery slot; the destination is absent.</summary>
    OriginalHeld,
    /// <summary>The exact destination exists and the original remains in its recovery slot.</summary>
    Published,
    /// <summary>Destination readback is exact and only recovery metadata cleanup remains.</summary>
    CleanupPending,
}
