namespace Kora.Core.Presentation;

/// <summary>Identifies the whole-source fallback, never a partially sanitized document.</summary>
public enum StaticHtmlFailure
{
    /// <summary>The complete fragment was projected.</summary>
    None,
    /// <summary>The source is not valid Unicode.</summary>
    InvalidUnicode,
    /// <summary>The source exceeds the parsing budget.</summary>
    ByteLimit,
    /// <summary>The element and text node budget was exceeded.</summary>
    NodeLimit,
    /// <summary>The element nesting budget was exceeded.</summary>
    DepthLimit,
    /// <summary>The block element budget was exceeded.</summary>
    BlockLimit,
    /// <summary>The entity reference budget was exceeded.</summary>
    EntityLimit,
    /// <summary>The complete semantic projection exceeds its budget.</summary>
    SemanticLimit,
    /// <summary>Markup is outside the strict inert fragment grammar.</summary>
    UnsupportedMarkup,
    /// <summary>The fragment is malformed.</summary>
    MalformedSource,
    /// <summary>The fragment has no readable projection.</summary>
    EmptyProjection,
}
