using Kora.Core.Providers;

namespace Kora.Core.Tools;

public sealed record ProviderSettingsInput(ProviderService Service, int Offset = 0, int Count = 6,
    string? Model = null, ProviderProfileTuple? Tuple = null, bool ClearModel = false);
