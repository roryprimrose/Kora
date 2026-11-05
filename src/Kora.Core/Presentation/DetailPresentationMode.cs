namespace Kora.Core.Presentation;

/// <summary>Device-local presentation of future detailed results.</summary>
public enum DetailPresentationMode
{
    /// <summary>Preserves the existing offer behavior.</summary>
    Offer,
    /// <summary>Retains manual access without proactive detail offers.</summary>
    LinkOnly,
    /// <summary>Reveals only new eligible foreground results without activation.</summary>
    OpenAutomatically,
}
