namespace Kora.Core.Dependencies;

/// <summary>An explicit choice for one new turn; it never changes an existing turn.</summary>
public enum ModelTurnChoice
{
    Default,
    Local,
    Hosted,
}
