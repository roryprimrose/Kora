namespace Kora.Core.Storage;

/// <summary>Describes whether the bounded observation finished.</summary>
public enum SessionRetentionPlanStatus
{
    /// <summary>Every admitted metadata candidate was validated and aggregated.</summary>
    Complete,
    /// <summary>A finite observation budget was exhausted; no aggregate is published.</summary>
    Incomplete,
    /// <summary>The source or its metadata could not be trusted; no aggregate is published.</summary>
    Unavailable,
}
