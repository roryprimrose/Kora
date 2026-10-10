namespace Kora.Core.Storage;

/// <summary>The lifecycle states included in passive session navigation.</summary>
public enum SessionListFilter
{
    /// <summary>Active and Done sessions; never Removed sessions.</summary>
    All,
    /// <summary>Active sessions only.</summary>
    Active,
    /// <summary>Done sessions only, without resuming them.</summary>
    Done
}
