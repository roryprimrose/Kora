namespace Kora.Core.KnowledgeSources;

/// <summary>The persisted admission state; only Enabled permits explicit local inspection.</summary>
public enum KnowledgeSourceState
{
    /// <summary>The exact durable snapshot is available, not automatically selected.</summary>
    Enabled,
    /// <summary>Use was explicitly withdrawn; refresh requires fresh review.</summary>
    Disabled,
    /// <summary>A refresh began; no previous revision may be used.</summary>
    Unavailable,
    /// <summary>Use is revoked and inventoried cleanup must be completed.</summary>
    Removing,
}
