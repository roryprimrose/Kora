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
    /// <summary>Open All work and complete a fresh bounded Needs answer observation.</summary>
    WaitingQuestions,
    /// <summary>Open an exact named observation from the foreground host-presented conversation page.</summary>
    ObservedResult,
    /// <summary>Read a bounded page from the exact selected session conversation.</summary>
    ConversationHistory,
    /// <summary>Show and focus the existing Presence settings without changing them.</summary>
    PresenceSettings,
    /// <summary>Show registered MCP metadata without inspecting endpoints or connecting.</summary>
    McpServers,
    /// <summary>Look up WorkIQ only in the registered MCP metadata.</summary>
    WorkIqMcp,
    /// <summary>Read an explicitly application-wide bounded page of committed authority audit.</summary>
    AuditHistory,
}
