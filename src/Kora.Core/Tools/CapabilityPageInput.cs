namespace Kora.Core.Tools;

public sealed record CapabilityPageInput(int Offset = 0, int Count = ReadOnlyCapabilityCatalog.MaximumRecords);
