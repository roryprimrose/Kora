using Kora.Core.Dependencies;

namespace Kora.Core.Providers;

public sealed class ProviderCatalogueModel
{
    public ProviderCatalogueModel(ProviderChoiceId id, string label, ModelProviderSelection selection,
        IReadOnlyList<ProviderProfileOption> reasoning, IReadOnlyList<ProviderProfileOption> context)
    {
        Id = id;
        Label = label;
        Selection = selection;
        Reasoning = Array.AsReadOnly(reasoning.ToArray());
        Context = Array.AsReadOnly(context.ToArray());
    }

    public ProviderChoiceId Id { get; }
    public string Label { get; }
    public ModelProviderSelection Selection { get; }
    public IReadOnlyList<ProviderProfileOption> Reasoning { get; }
    public IReadOnlyList<ProviderProfileOption> Context { get; }
}
