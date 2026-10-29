namespace Kora.Core.KnowledgeSources;

/// <summary>Inspects only one explicitly selected named root under its declared traversal policy.</summary>
public interface IKnowledgeSourceInspector
{
    /// <summary>Retains the complete metadata inventory without reading file bodies.</summary>
    Task<IKnowledgeSourceSelection> InspectSourceAsync(string selectedPath, KnowledgeSourceScope scope,
        CancellationToken cancellationToken);
}
