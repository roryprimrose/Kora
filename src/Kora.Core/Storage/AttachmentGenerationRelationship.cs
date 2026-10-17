namespace Kora.Core.Storage;

/// <summary>A passive relationship between retained captured and observed session generations.</summary>
public enum AttachmentGenerationRelationship
{
    /// <summary>No generation relationship constraint.</summary>
    Any,
    /// <summary>The retained captured generation equals the observed session generation.</summary>
    SameObservedGeneration,
    /// <summary>The retained captured generation precedes the observed session generation.</summary>
    EarlierObservedGeneration
}
