namespace Kora.Core.Dependencies;

/// <summary>The host-owned tool policy captured for one complete turn, never selected by provider output.</summary>
public enum ModelToolMode
{
    /// <summary>Only the existing bounded read-only catalogue may be invoked through host admission.</summary>
    ReadOnly,

    /// <summary>No tool declarations, calls, results or dispatch are admitted.</summary>
    None,

    /// <summary>One explicitly approved public page, returned only to the original local model turn.</summary>
    LocalWebPage,

    /// <summary>Only the exact reviewed dynamic tool; retrieved data stays in the original local model turn.</summary>
    McpReadOnly,

    /// <summary>Only a separately reviewed bounded Active-session metadata page in the original local turn.</summary>
    SessionDiscovery,

    /// <summary>Only the frozen reviewed customization metadata selection in the original local turn.</summary>
    CustomizationDiscovery,
}
