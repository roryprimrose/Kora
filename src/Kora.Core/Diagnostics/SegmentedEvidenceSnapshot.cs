namespace Kora.Core.Diagnostics;

public sealed record SegmentedEvidenceSnapshot(int Version, string StoreIdentity, long InventoryGeneration,
    IReadOnlyList<EvidenceSegmentSnapshot> Segments)
{
    public const int CurrentVersion = 1;
}
