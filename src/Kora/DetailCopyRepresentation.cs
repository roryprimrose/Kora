namespace Kora;

/// <summary>Chooses the active representation or the original admitted source.</summary>
internal enum DetailCopyRepresentation
{
    /// <summary>Copies the complete active view or its selected range.</summary>
    ActiveView,
    /// <summary>Copies the complete unchanged source, without rich formats.</summary>
    ExactSource,
}
