namespace Kora.Windows.Storage;

internal sealed record EvidenceSegmentReadSet(
    string StoreIdentity, long InventoryGeneration, IReadOnlyList<EvidenceSegmentReadView> Segments);
