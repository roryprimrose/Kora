using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kora.Core.Providers;

public sealed class ProviderProfilePreferences
{
    public const int MaximumUtf8Bytes = 8192;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public ProviderProfilePreferences(ProviderService service, IReadOnlyList<ProviderProfileTuple> tuples)
    {
        if (!Enum.IsDefined(service) || tuples.Count > 4 || tuples.Any(item => item is null)
            || tuples.Select(item => item.Service).Distinct().Count() != tuples.Count)
        { throw new InvalidDataException("Invalid provider selections."); }
        foreach (var tuple in tuples) { ProviderProfileRules.ValidateTuple(tuple); }
        Service = service;
        Tuples = Array.AsReadOnly(tuples.ToArray());
    }

    public ProviderService Service { get; }
    public IReadOnlyList<ProviderProfileTuple> Tuples { get; }
    public ProviderProfileTuple? For(ProviderService service) => Tuples.SingleOrDefault(item => item.Service == service);
    public ProviderProfilePreferences Select(ProviderService service) => new(service, Tuples);
    public ProviderProfilePreferences Select(ProviderProfileTuple tuple) =>
        new(Service, Tuples.Where(item => item.Service != tuple.Service).Append(tuple).OrderBy(item => item.Service).ToArray());
    public ProviderProfilePreferences Reset(ProviderService service) => new(Service, Tuples.Where(item => item.Service != service).ToArray());
    public string Serialize() => JsonSerializer.Serialize(new Format(1, Service, Tuples.ToArray()), JsonOptions);

    public static ProviderProfilePreferences Parse(string text)
    {
        try
        {
            if (Encoding.UTF8.GetByteCount(text) > MaximumUtf8Bytes) { throw new InvalidDataException("Provider preferences exceed the byte limit."); }
            var value = JsonSerializer.Deserialize<Format>(text, JsonOptions);
            if (value is null || value.Schema != 1 || value.Tuples is null)
            { throw new InvalidDataException("Unknown provider preference format."); }
            var preferences = new ProviderProfilePreferences(value.Service, value.Tuples);
            if (!string.Equals(preferences.Serialize(), text, StringComparison.Ordinal))
            { throw new InvalidDataException("Provider preferences are not canonical schema-1 data."); }
            return preferences;
        }
        catch (JsonException exception) { throw new InvalidDataException("Malformed provider preferences.", exception); }
        catch (ArgumentException exception) { throw new InvalidDataException("Invalid provider preference binding.", exception); }
    }

    private sealed record Format(
        [property: JsonRequired] int Schema,
        [property: JsonRequired] ProviderService Service,
        [property: JsonRequired] ProviderProfileTuple[] Tuples);
}
