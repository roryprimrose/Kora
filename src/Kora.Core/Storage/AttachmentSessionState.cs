namespace Kora.Core.Storage;

/// <summary>Stored lifecycle, independent of attachment availability and execution state.</summary>
public enum AttachmentSessionState
{
    /// <summary>Stored active session.</summary>
    Active,
    /// <summary>Stored completed session; inspection does not resume it.</summary>
    Done,
    /// <summary>Removed session with content-free provenance only.</summary>
    Removed,
}
