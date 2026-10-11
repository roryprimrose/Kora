using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kora.Core.Providers;

/// <summary>Exact sparse desired session policy. Null fields inherit; a binding never supplies execution authority.</summary>
public sealed class SessionProviderOverrides
{
    public const int MaximumUtf8Bytes = 4096;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public SessionProviderOverrides(ProviderService? service = null, ProviderChoiceId? model = null,
        ProviderChoiceId? reasoning = null, ProviderChoiceId? context = null, ProviderProfileTuple? binding = null)
    {
        if (service is { } selected && !Enum.IsDefined(selected)
            || (model is not null || reasoning is not null || context is not null) != (binding is not null))
        { throw new InvalidDataException("Sparse provider choices require exact catalogue binding, or all dependent fields inherited."); }
        if (binding is not null)
        {
            ProviderProfileRules.ValidateTuple(binding);
            if (service is { } boundService && boundService != binding.Service
                || model is not null && model != binding.Model
                || reasoning is not null && reasoning != binding.Reasoning
                || context is not null && context != binding.Context)
            { throw new InvalidDataException("Sparse provider choices conflict with their original advertised binding."); }
        }
        Service = service;
        Model = model;
        Reasoning = reasoning;
        Context = context;
        Binding = binding;
    }

    public ProviderService? Service { get; }
    public ProviderChoiceId? Model { get; }
    public ProviderChoiceId? Reasoning { get; }
    public ProviderChoiceId? Context { get; }
    public ProviderProfileTuple? Binding { get; }
    public static SessionProviderOverrides Inherit { get; } = new();
    public ProviderService EffectiveService(ProviderProfilePreferences app) => Service ?? app.Service;

    public ProviderProfileTuple? ResolveDesired(ProviderProfilePreferences app)
    {
        var service = EffectiveService(app);
        var inherited = app.For(service);
        if (service == ProviderService.Unset) { return null; }
        if (Binding is not { } binding) { return inherited; }
        if (binding.Service != service
            || (Model is null || Reasoning is null || Context is null) && inherited is not null
                && (inherited.Account != binding.Account || inherited.CatalogueRevision != binding.CatalogueRevision))
        { return null; }
        var model = Model ?? inherited?.Model;
        var reasoning = Reasoning ?? inherited?.Reasoning;
        var context = Context ?? inherited?.Context;
        // Dependent option IDs are model-specific; matching labels/IDs on another model cannot carry authority.
        return model is null || reasoning is null || context is null || model != binding.Model ? null
            : new(service, binding.Account, binding.CatalogueRevision, model, reasoning, context);
    }

    public SessionProviderOverrides Reset(SessionProviderField field)
    {
        if (!Enum.IsDefined(field)) { throw new InvalidDataException("Unknown session provider field."); }
        var model = field == SessionProviderField.Model ? null : Model;
        var reasoning = field == SessionProviderField.Reasoning ? null : Reasoning;
        var context = field == SessionProviderField.Context ? null : Context;
        return new(field == SessionProviderField.Service ? null : Service, model, reasoning, context,
            model is null && reasoning is null && context is null ? null : Binding);
    }

    public SessionProviderOverrides Select(ProviderProfileTuple binding, SessionProviderField? field)
    {
        ProviderProfileRules.ValidateTuple(binding);
        if (field == SessionProviderField.Service || field is { } selected && !Enum.IsDefined(selected))
        { throw new InvalidDataException("Tuple selection changes model/reasoning/context only."); }
        var preservesBoundField = field is not null && (field != SessionProviderField.Model && Model is not null
            || field != SessionProviderField.Reasoning && Reasoning is not null || field != SessionProviderField.Context && Context is not null);
        if (preservesBoundField)
        {
            // Construction requires a binding whenever any dependent field is explicit.
            var previous = Binding!;
            if (previous.Service != binding.Service || previous.Account != binding.Account
                || previous.CatalogueRevision != binding.CatalogueRevision || previous.Model != binding.Model)
            { throw new InvalidDataException("Reset dependent fields or explicitly replace the complete tuple; matching IDs cannot silently rebind other overrides."); }
        }
        return new(Service, field is null or SessionProviderField.Model ? binding.Model : Model,
            field is null or SessionProviderField.Reasoning ? binding.Reasoning : Reasoning,
            field is null or SessionProviderField.Context ? binding.Context : Context, binding);
    }

    public string Serialize() => JsonSerializer.Serialize(new Format(1, Service, Model, Reasoning, Context, Binding), JsonOptions);
    public static SessionProviderOverrides Parse(string text)
    {
        try
        {
            if (Encoding.UTF8.GetByteCount(text) > MaximumUtf8Bytes) { throw new InvalidDataException("Session provider policy exceeds its byte bound."); }
            var value = JsonSerializer.Deserialize<Format>(text, JsonOptions);
            if (value is null || value.Schema != 1) { throw new InvalidDataException("Unknown session provider policy format."); }
            var policy = new SessionProviderOverrides(value.Service, value.Model, value.Reasoning, value.Context, value.Binding);
            if (!string.Equals(policy.Serialize(), text, StringComparison.Ordinal)) { throw new InvalidDataException("Noncanonical session provider policy."); }
            return policy;
        }
        catch (JsonException exception) { throw new InvalidDataException("Malformed session provider policy.", exception); }
        catch (ArgumentException exception) { throw new InvalidDataException("Invalid session provider binding.", exception); }
    }

    private sealed record Format([property: JsonRequired] int Schema,
        [property: JsonRequired] ProviderService? Service, [property: JsonRequired] ProviderChoiceId? Model,
        [property: JsonRequired] ProviderChoiceId? Reasoning, [property: JsonRequired] ProviderChoiceId? Context,
        [property: JsonRequired] ProviderProfileTuple? Binding);
}
