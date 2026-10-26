namespace Kora.Core.Tools;

/// <summary>A page entirely within one original user's frozen reviewed selection.</summary>
public sealed record CustomizationMetadataPage(
    IReadOnlyList<CustomizationMetadata> Records, int TotalRecords, int? NextOffset);
