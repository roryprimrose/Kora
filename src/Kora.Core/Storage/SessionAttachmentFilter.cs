namespace Kora.Core.Storage;

/// <summary>Current stored session lifecycle included in the attachment metadata catalogue.</summary>
public enum SessionAttachmentFilter
{
    /// <summary>All stored states, including Removed-session tombstones when removed slots are included.</summary>
    All,
    /// <summary>Active sessions, independently of attachment slot removal.</summary>
    Active,
    /// <summary>Done sessions without resuming them.</summary>
    Done,
    /// <summary>Removed sessions; only content-free removed slots can remain valid.</summary>
    Removed
}
