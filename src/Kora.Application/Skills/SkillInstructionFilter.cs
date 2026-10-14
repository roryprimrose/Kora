namespace Kora.Application.Skills;

/// <summary>Shared structural instruction compatibility, independent of invocation availability.</summary>
public enum SkillInstructionFilter
{
    /// <summary>Include compatible, incompatible and unavailable snapshots.</summary>
    All,
    /// <summary>Include structurally instruction-compatible snapshots.</summary>
    Compatible,
    /// <summary>Include structurally instruction-incompatible snapshots.</summary>
    Incompatible,
}
