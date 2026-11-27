namespace Kora.Windows.Input;

/// <summary>Message delivered to the dedicated hotkey thread.</summary>
internal enum GlobalPttNativeMessage
{
    /// <summary>A posted owner command.</summary>
    Wake,
    /// <summary>The registered chord was pressed.</summary>
    Press,
    /// <summary>A held-only release observation is due.</summary>
    CheckRelease,
}
