namespace Kora.Core.KnowledgeSources;

/// <summary>Inspects only one explicitly selected named root under its declared traversal policy.</summary>
public interface IKnowledgeSourceInspector
{
    /// <summary>Retains the complete metadata inventory without reading file bodies.</summary>
    Task<IKnowledgeSourceSelection> InspectSourceAsync(string selectedPath, KnowledgeSourceScope scope,
        CancellationToken cancellationToken);

    Task<IKnowledgeSourceSelection> InspectSourceAsync(string selectedPath, KnowledgeSourceScope scope,
        KnowledgeSourceCaptureLimits limits, CancellationToken cancellationToken)
    {
        if (limits != KnowledgeSourceCaptureLimits.HardMaximum)
        { throw new InvalidOperationException("This inspector does not support lowered exact-source capture limits."); }
        return InspectSourceAsync(selectedPath, scope, cancellationToken);
    }
}
