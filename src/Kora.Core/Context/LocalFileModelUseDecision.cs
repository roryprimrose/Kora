namespace Kora.Core.Context;

/// <summary>The source-use policy decision; unknown values never authorize model use.</summary>
public enum LocalFileModelUseDecision
{
    Unavailable, Denied, Allowed,
}
