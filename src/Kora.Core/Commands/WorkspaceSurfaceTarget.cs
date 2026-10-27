namespace Kora.Core.Commands;

/// <summary>Existing passive native surfaces; not session or execution authority.</summary>
public enum WorkspaceSurfaceTarget
{
    /// <summary>Reserved navigation input that is not an exact command.</summary>
    Invalid,
    /// <summary>The bounded All work observer.</summary>
    AllWork,
    /// <summary>The existing selected-session compact projection.</summary>
    CompactConversation,
    /// <summary>The expanded primary interaction window and its bounded session list.</summary>
    Sessions,
    /// <summary>Select an exact row from the already presented session list.</summary>
    SelectSession,
    Details,
    HideDetails,
    SessionLogs,
    SessionAudit,
    OpenNamedConversation,
}
