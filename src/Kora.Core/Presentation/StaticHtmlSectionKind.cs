namespace Kora.Core.Presentation;

/// <summary>Describes the native semantic section, not document-controlled styling.</summary>
public enum StaticHtmlSectionKind
{
    /// <summary>Ordinary text.</summary>
    Paragraph,
    /// <summary>A first-level heading.</summary>
    Heading1,
    /// <summary>A second-level heading.</summary>
    Heading2,
    /// <summary>A third-level heading.</summary>
    Heading3,
    /// <summary>A fourth-level heading.</summary>
    Heading4,
    /// <summary>A fifth-level heading.</summary>
    Heading5,
    /// <summary>A sixth-level heading.</summary>
    Heading6,
    /// <summary>Preformatted inert code.</summary>
    Code,
    /// <summary>A labelled list item.</summary>
    ListItem,
    /// <summary>A labelled quotation.</summary>
    Quote,
    /// <summary>A textual thematic separator.</summary>
    Divider,
}
