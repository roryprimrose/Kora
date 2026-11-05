using Kora.Core.Providers;
using Kora.Core.Tools;

namespace Kora.Tools.Providers;

public sealed class ProviderSettingsPropose
{
    internal ProviderProfilePreferences Execute(ProviderProfilePreferences desired, ProviderCatalogueObservation observation,
        ProviderSettingsInput input, DateTimeOffset now)
    {
        var result = desired.Select(input.Service);
        if (input.Tuple is { } tuple)
        {
            if (input.ClearModel || tuple.Service != input.Service
                || tuple.Service == ProviderService.Local && observation.Installation != LocalProviderInstallation.Installed
                || ProviderProfileRules.Resolve(tuple, observation.For(input.Service), now) is null)
            { throw new InvalidOperationException("The proposed complete tuple is not current. No preference was written or substituted."); }
            return result.Select(tuple);
        }
        return input.ClearModel ? result.Reset(input.Service) : result;
    }
}
