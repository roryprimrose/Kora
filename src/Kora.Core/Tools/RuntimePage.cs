namespace Kora.Core.Tools;

public sealed record RuntimePage(IReadOnlyList<RuntimeObservation> Records, int TotalRecords, int? NextOffset);
