namespace Kora.Core.Storage;

/// <summary>Copy certification metadata, never a claim that body bytes were verified by listing.</summary>
public enum AttachmentCopyStatus
{
    /// <summary>The stored capture or replacement has its terminal copy authority.</summary>
    Confirmed,
    /// <summary>Replacement has no terminal copy certification; neither body may be disclosed.</summary>
    Held,
    /// <summary>The retained slot has been revoked; body and original metadata are absent.</summary>
    Unavailable,
}
