namespace Kora.Core.Tools;

public sealed record ReadinessPage(IReadOnlyList<ReadinessObservation> Records, int TotalRecords, int? NextOffset);
