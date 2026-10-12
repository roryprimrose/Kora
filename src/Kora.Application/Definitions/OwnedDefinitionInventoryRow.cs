using System.Globalization;

using Kora.Core.Artifacts;

namespace Kora.Application.Definitions;

public sealed class OwnedDefinitionInventoryRow
{
    internal OwnedDefinitionInventoryRow(ArtifactKind kind, string name, ArtifactDefinition? candidate, DateTimeOffset observedUtc)
    {
        Kind = kind;
        Name = name;
        CanonicalFileName = name + OwnedDefinitionKind.Extension(kind);
        DisplayName = candidate?.Name;
        ArtifactId = candidate?.Id;
        Digest = candidate?.Digest;
        NativeRevision = candidate?.OwnedInstructionRevision;
        ObservedUseEnabled = NativeRevision is null ? null : candidate!.IsUseEnabled;
        ObservedUtc = observedUtc;
    }

    public ArtifactKind Kind { get; }
    public string Name { get; }
    public string CanonicalFileName { get; }
    public string? DisplayName { get; }
    public string Source => "kora-profile";
    public string? ArtifactId { get; }
    public string? Digest { get; }
    public string? NativeRevision { get; }
    public bool? ObservedUseEnabled { get; }
    public DateTimeOffset ObservedUtc { get; }
    public string UseState => ObservedUseEnabled switch
    {
        true => "Use enabled (observed)",
        false => "Review required (observed)",
        null => "Unavailable or unknown",
    };
    public string Details => $"Kind: {Kind}; filename ID: {CanonicalFileName}; display name: {DisplayName ?? "(unmapped)"}; "
        + $"source: {Source}; artifact ID: {ArtifactId ?? "(unmapped)"}; digest: {Digest ?? "(unknown)"}; "
        + $"native revision: {NativeRevision ?? "(unknown)"}; observed UTC: {ObservedUtc.ToString("O", CultureInfo.InvariantCulture)}; {UseState}.";
    public override string ToString() => CanonicalFileName + " — " + UseState;
}
