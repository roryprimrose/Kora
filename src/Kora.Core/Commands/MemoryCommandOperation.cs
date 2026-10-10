namespace Kora.Core.Commands;

/// <summary>Exact original-user session memory operations; no model-use route.</summary>
public enum MemoryCommandOperation
{
    /// <summary>Reserved memory grammar that failed validation.</summary>
    Invalid,
    /// <summary>Displays the bounded command syntax.</summary>
    Help,
    /// <summary>Lists content-free metadata for one exact active session.</summary>
    List,
    /// <summary>Creates a volatile user proposal without an existing memory identity or review.</summary>
    Propose,
    /// <summary>Inspects the exact current value, classification and lineage.</summary>
    Inspect,
    /// <summary>Accepts or rejects a previously inspected proposed revision.</summary>
    Review,
    /// <summary>Separately admits a reviewed pending revision.</summary>
    Admit,
    /// <summary>Replaces the value and clears review, returning to Proposed/Pending.</summary>
    Edit,
    /// <summary>Disables an enabled revision without erasing its reviewed content.</summary>
    Disable,
    /// <summary>Forgets content while retaining a non-reusable identity tombstone.</summary>
    Forget,
}
