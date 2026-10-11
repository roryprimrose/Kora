namespace Kora.Core.Context;

/// <summary>Run-only chunk selection for an exact already admitted snapshot, never read or retention authority.</summary>
public enum LocalFileRetrievalMode
{
    /// <summary>The original heading/paragraph/line retrieval policy.</summary>
    LexicalLines = 0,

    /// <summary>The bounded inert Markdown section and fence scanner; plain text uses the original line chunks.</summary>
    MarkdownStructure = 1,
}
