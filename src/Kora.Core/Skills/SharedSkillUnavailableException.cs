namespace Kora.Core.Skills;

public sealed class SharedSkillUnavailableException(string reasonCode) : IOException
{
    public string ReasonCode { get; } = reasonCode;
}
