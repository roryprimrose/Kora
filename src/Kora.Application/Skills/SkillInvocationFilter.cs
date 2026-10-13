namespace Kora.Application.Skills;

/// <summary>Observed bundled invocation availability, not instruction compatibility or authority.</summary>
public enum SkillInvocationFilter
{
    /// <summary>Include every admitted snapshot.</summary>
    All,
    /// <summary>Include snapshots whose runtime invocation is unavailable.</summary>
    Unavailable,
    /// <summary>Include snapshots whose runtime invocation is available.</summary>
    Available,
}
