namespace Kora.Core.Dependencies;

/// <summary>Ownership of the exact data directory, independently of runtime selection.</summary>
public enum SpeechComponentOwnership
{
    /// <summary>No exact component directory exists.</summary>
    Missing,
    /// <summary>The exact host manifest and directory contents match managed ownership.</summary>
    Managed,
    /// <summary>Existing data cannot be changed by Kora's setup operation.</summary>
    Foreign,
    /// <summary>An interrupted exact removal requires explicit reconciliation.</summary>
    RemovalPending,
}
