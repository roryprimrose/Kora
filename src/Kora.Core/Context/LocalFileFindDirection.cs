namespace Kora.Core.Context;

/// <summary>Explicit nonoverlapping, nonwrapping navigation in one original source.</summary>
public enum LocalFileFindDirection
{
    /// <summary>Scan from original offset zero.</summary>
    First,
    /// <summary>Scan after the selected match's exclusive end.</summary>
    Next,
    /// <summary>Find the last match ending at or before the selected match's start.</summary>
    Previous,
}