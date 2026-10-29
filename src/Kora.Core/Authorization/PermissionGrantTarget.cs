using Kora.Core.Customizations;

namespace Kora.Core.Authorization;

/// <summary>Host-recorded target metadata for presentation, never independent execution authority.</summary>
public sealed record PermissionGrantTarget
{
    public PermissionGrantTarget(PermissionGrantArtifactKind kind, CustomizationScope? scope = null)
    {
        if (!Enum.IsDefined(kind) || scope is { } known && !Enum.IsDefined(known))
        {
            throw new InvalidDataException("The permission grant target kind or artefact scope is invalid.");
        }
        Kind = kind;
        Scope = scope;
    }

    public PermissionGrantArtifactKind Kind { get; }
    public CustomizationScope? Scope { get; }
}
