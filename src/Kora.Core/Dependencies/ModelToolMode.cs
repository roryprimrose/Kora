namespace Kora.Core.Dependencies;

/// <summary>The host-owned tool policy captured for one complete turn, never selected by provider output.</summary>
public enum ModelToolMode
{
    /// <summary>Only the existing bounded read-only catalogue may be invoked through host admission.</summary>
    ReadOnly,

    /// <summary>No tool declarations, calls, results or dispatch are admitted.</summary>
    None,
}
