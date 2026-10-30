namespace Kora.Core.Storage;

/// <summary>The exact consequence of applying a confirmed policy without renewing activity.</summary>
public enum SessionRetentionApplyEffect
{
    DeadlinesOnly,
    Archive,
    Delete,
}
