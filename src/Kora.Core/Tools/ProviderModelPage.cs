namespace Kora.Core.Tools;

public sealed record ProviderModelPage(ProviderServiceAvailability Service, IReadOnlyList<ProviderModelSummary> Records, int? NextOffset);
