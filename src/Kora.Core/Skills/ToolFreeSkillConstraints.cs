using Kora.Core.Dependencies;

namespace Kora.Core.Skills;

/// <summary>Host budgets for one reviewed instruction-only local skill answer.</summary>
public static class ToolFreeSkillConstraints
{
    public static ModelTurnConstraints Instance { get; } = new(1, [], 0, 1,
        ModelContextEnvelope.MaximumInputUtf8Bytes, ModelContextEnvelope.MaximumOutputUtf8Bytes,
        (int)ModelToolProtocol.Deadline.TotalMilliseconds);
}
