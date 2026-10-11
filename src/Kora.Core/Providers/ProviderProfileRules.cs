using System.Text;
using Kora.Core.Dependencies;

namespace Kora.Core.Providers;

public static class ProviderProfileRules
{
    public static ProviderProfileTuple ReplaceField(ProviderProfileTuple tuple, ProviderProfileField field, ProviderChoiceId id)
    {
        ValidateTuple(tuple);
        ArgumentNullException.ThrowIfNull(id);
        return field switch
        {
            ProviderProfileField.Model => tuple with { Model = id },
            ProviderProfileField.Reasoning => tuple with { Reasoning = id },
            ProviderProfileField.Context => tuple with { Context = id },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };
    }

    public static void ValidateId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(character => character is < '!' or > '~'))
        { throw new InvalidDataException("Provider IDs must be exact bounded opaque ASCII values."); }
    }

    public static void ValidateTuple(ProviderProfileTuple? tuple)
    {
        if (tuple is null || tuple.Service is not (ProviderService.Local or ProviderService.Copilot or ProviderService.Claude or ProviderService.Codex)
            || tuple.Account is null || tuple.CatalogueRevision.Value <= 0
            || tuple.Model is null || tuple.Reasoning is null || tuple.Context is null)
        { throw new InvalidDataException("A complete account/catalogue-bound provider tuple is required."); }
    }

    public static void ValidateCatalogue(ProviderCapabilityCatalogue catalogue)
    {
        if (catalogue.Service is not (ProviderService.Local or ProviderService.Copilot or ProviderService.Claude or ProviderService.Codex)
            || !Enum.IsDefined(catalogue.Status) || catalogue.Revision.Value <= 0 || catalogue.ValidUntil <= catalogue.ObservedAt
            || catalogue.Models.Count > 64 || catalogue.Models.Select(model => model.Id).Distinct().Count() != catalogue.Models.Count
            || catalogue.Models.Select(model => model.Selection).Distinct().Count() != catalogue.Models.Count
            || catalogue.Status != ProviderCatalogueStatus.Current && catalogue.Models.Count != 0
            || catalogue.Status == ProviderCatalogueStatus.Current && catalogue.Account is null)
        { throw new InvalidDataException("The provider catalogue is inconsistent or incomplete."); }
        foreach (var model in catalogue.Models)
        {
            ValidateLabel(model.Label);
            if (model.Id is null || !model.Selection.IsValid || model.Selection.Revision != catalogue.Revision || ServiceFor(model.Selection.Provider) != catalogue.Service)
            { throw new InvalidDataException("The catalogue model belongs to another service."); }
            ValidateOptions(model.Reasoning, context: false);
            ValidateOptions(model.Context, context: true);
        }
    }

    private static void ValidateOptions(IReadOnlyList<ProviderProfileOption> options, bool context)
    {
        if (options.Count is < 1 or > 16 || options.Select(option => option.Id).Distinct().Count() != options.Count)
        { throw new InvalidDataException("Catalogue options are incomplete, duplicate or exceed their bound."); }
        if (options.Count != 1 && options.Any(option => option.ProviderManaged))
        { throw new InvalidDataException("Provider-managed controls must advertise one fixed exact value, not selectable substitutes."); }
        foreach (var option in options)
        {
            ValidateLabel(option.Label);
            if (option.Id is null || option.InputTokenLimit is <= 0
                || !context && option.InputTokenLimit is not null
                || option.ProviderManaged && option.InputTokenLimit is not null)
            { throw new InvalidDataException("Invalid provider-managed or input budget option."); }
        }
    }

    private static void ValidateLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label) || Encoding.UTF8.GetByteCount(label) > 256 || label.Any(char.IsControl))
        { throw new InvalidDataException("Provider display labels must be bounded inert text."); }
    }

    public static ProviderService ServiceFor(ModelProviderIdentity provider) => provider switch
    {
        ModelProviderIdentity.Ollama => ProviderService.Local,
        ModelProviderIdentity.Copilot => ProviderService.Copilot,
        ModelProviderIdentity.Claude => ProviderService.Claude,
        ModelProviderIdentity.Codex => ProviderService.Codex,
        _ => ProviderService.Unset,
    };

    public static ProviderCatalogueModel? Resolve(ProviderProfileTuple? tuple, ProviderCapabilityCatalogue catalogue, DateTimeOffset now)
    {
        if (tuple is null || !catalogue.IsCurrent(now) || tuple.Service != catalogue.Service
            || tuple.Account != catalogue.Account || tuple.CatalogueRevision != catalogue.Revision)
        { return null; }
        return catalogue.Models.SingleOrDefault(model => model.Id == tuple.Model
            && model.Reasoning.Any(option => option.Id == tuple.Reasoning) && model.Context.Any(option => option.Id == tuple.Context));
    }
}
