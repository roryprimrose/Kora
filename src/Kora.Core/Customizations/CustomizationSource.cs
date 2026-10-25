namespace Kora.Core.Customizations;

public sealed record CustomizationSource(
    string Id, CustomizationKind Kind, CustomizationScope Scope, string Name,
    string Location, string PrimaryFile, bool IsReadOnly = false, string? UnavailableReason = null);
