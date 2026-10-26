namespace Kora.Core.Customizations;

/// <summary>Read-only selected-source metadata; never a package-content or use-control interface.</summary>
public interface ICustomizationMetadataStore
{
    /// <summary>Lists only supported skill, agent and instruction source choices, without reading their bodies or MCP configuration.</summary>
    Task<IReadOnlyList<CustomizationSource>> ListMetadataSources(CancellationToken token);

    /// <summary>Reads the primary definition of one exact source and returns only its validated metadata and internal currentness identity.</summary>
    Task<CustomizationMetadataObservation> ReadMetadata(CustomizationSource source, CancellationToken token);
}
