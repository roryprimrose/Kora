namespace Kora.Core.Presentation;

/// <summary>Device-local presentation of existing passive detail offers; automatic opening is unavailable.</summary>
public enum DetailPresentationMode
{
    /// <summary>Preserves the existing offer behavior.</summary>
    Offer,
    /// <summary>Retains manual access without proactive detail offers.</summary>
    LinkOnly,
}
