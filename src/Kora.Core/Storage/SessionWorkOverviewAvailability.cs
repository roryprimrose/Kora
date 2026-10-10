namespace Kora.Core.Storage;

/// <summary>Explicit observation availability; unavailable rows are not empty work or filter nonmatches.</summary>
public enum SessionWorkOverviewAvailability
{
    /// <summary>The exact session's independent work snapshot was observed.</summary>
    Observed,
    /// <summary>Metadata and work no longer agree on the exact generation, lifecycle or metadata revision.</summary>
    SourceChanged,
    /// <summary>The source refused or failed this exact session observation.</summary>
    ReadUnavailable,
    /// <summary>One complete session projection cannot fit the output budget; no task details were silently truncated.</summary>
    OutputBound
}
