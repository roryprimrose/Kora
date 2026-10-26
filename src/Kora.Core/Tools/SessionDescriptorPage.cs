namespace Kora.Core.Tools;

public sealed record SessionDescriptorPage(IReadOnlyList<SessionDescriptor> Records, int TotalRecords, int? NextOffset);
