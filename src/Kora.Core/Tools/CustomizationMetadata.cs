using Kora.Core.Customizations;

namespace Kora.Core.Tools;

/// <summary>Closed metadata projection. Inclusion and compatibility never establish executable readiness.</summary>
public sealed record CustomizationMetadata(
    string Ref, CustomizationKind Kind, string Name, string Description, CustomizationScope SourceCategory,
    bool Compatible, bool Enabled, CapabilityAvailability Availability);
