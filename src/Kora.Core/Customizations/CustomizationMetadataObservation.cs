namespace Kora.Core.Customizations;

/// <summary>Host-only observation. Identity, revision and source location must never enter model context.</summary>
public sealed record CustomizationMetadataObservation(
    CustomizationSource Source, string Revision, string Name, string Description, string ArtifactId, string Digest);
