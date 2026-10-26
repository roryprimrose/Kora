namespace Kora;

/// <summary>Host-selected rendering profiles; document content cannot select or enable a profile.</summary>
internal enum NativeDetailFormat
{
    /// <summary>The unchanged bounded CommonMark text subset.</summary>
    TextV1,

    /// <summary>The additive, separately gated pipe-table text profile.</summary>
    PipeTablesV2,

    /// <summary>The separately gated, display-only Markdown checklist profile.</summary>
    MarkdownChecklistsV3,

    /// <summary>The separately gated, passive double-tilde strikethrough profile.</summary>
    MarkdownStrikethroughV4,
}
