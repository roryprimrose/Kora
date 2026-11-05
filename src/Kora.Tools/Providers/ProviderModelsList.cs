using Kora.Core.Providers;
using Kora.Core.Tools;

namespace Kora.Tools.Providers;

public sealed class ProviderModelsList
{
    internal ProviderModelPage Execute(ProviderCatalogueObservation observation, ProviderSettingsInput input, DateTimeOffset now)
    {
        var catalogue = observation.For(input.Service);
        var state = ProviderSettingsGet.Availability(catalogue, observation.Installation, now);
        if (input.Offset < 0 || input.Count is < 1 or > 6 || input.Offset > state.ModelCount
            || input.Offset != 0 && input.Offset == state.ModelCount)
        { throw new InvalidDataException("Provider model page is out of range."); }
        var count = Math.Min(input.Count, state.ModelCount - input.Offset);
        return new(state, state.Status != ProviderCatalogueStatus.Current ? []
            : catalogue.Models.Skip(input.Offset).Take(count).Select(model => new ProviderModelSummary(model.Id, model.Label)).ToArray(),
            input.Offset + count < state.ModelCount ? input.Offset + count : null);
    }
}
