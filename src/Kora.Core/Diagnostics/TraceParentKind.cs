namespace Kora.Core.Diagnostics;

/// <summary>Restricts diagnostic correlation to observed retained parent metadata.</summary>
public enum TraceParentKind
{
    /// <summary>Includes records with or without retained trace metadata.</summary>
    Any,
    /// <summary>Requires a valid retained trace whose parent span ID is null.</summary>
    ObservedRoot,
    /// <summary>Requires a valid retained trace whose parent span ID is nonnull.</summary>
    ObservedChild,
}
