namespace Kora.Core.Commands;

/// <summary>Existing passive native surfaces; not session or execution authority.</summary>
public enum WorkspaceSurfaceTarget
{
    /// <summary>Reserved navigation input that is not an exact command.</summary>
    Invalid,
    /// <summary>The bounded All work observer.</summary>
    AllWork,
    /// <summary>The metadata-only saved conversation draft catalogue.</summary>
    SavedConversationDrafts,
    /// <summary>The existing selected-session compact projection.</summary>
    CompactConversation,
}
