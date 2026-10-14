namespace Kora.Core.Context;

/// <summary>Explicit nonoverlapping, nonwrapping navigation in immutable text.</summary>
public enum SnapshotFindDirection
{
    /// <summary>Scan from offset zero.</summary>
    First,
    /// <summary>Scan after the selected span's exclusive end.</summary>
    Next,
    /// <summary>Find the last span ending at or before the selected start.</summary>
    Previous,
}
