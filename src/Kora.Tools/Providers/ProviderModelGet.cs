using Kora.Core.Providers;
using Kora.Core.Tools;

namespace Kora.Tools.Providers;

public sealed class ProviderModelGet
{
    internal ProviderModelDetails Execute(ProviderCatalogueObservation observation, ProviderSettingsInput input, DateTimeOffset now)
    {
        var catalogue = observation.For(input.Service);
        var state = ProviderSettingsGet.Availability(catalogue, observation.Installation, now);
        var model = state.Status == ProviderCatalogueStatus.Current
            ? catalogue.Models.SingleOrDefault(item => string.Equals(item.Id.Value, input.Model, StringComparison.Ordinal)) : null;
        return model is not null ? new(state, model)
            : throw new InvalidOperationException("The exact model is unavailable. Refresh or explicitly select another model; no substitute was chosen.");
    }
}
