namespace Kora.Core.Providers;

public sealed record ProviderProfileOption(
    ProviderChoiceId Id, string Label, bool ProviderManaged, int? InputTokenLimit = null);
