namespace Kora.Core.KnowledgeSources;

/// <summary>Explicit named-source discovery consent; never inferred from an existing folder review.</summary>
public enum KnowledgeSourceScope
{
    /// <summary>Only immediate files; subdirectories reject the source.</summary>
    ImmediateFiles = 0,
    /// <summary>The complete bounded descendant tree, including empty directories.</summary>
    BoundedDescendants = 1,
}
