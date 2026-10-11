namespace Kora.Core.Providers;

/// <summary>An exact opaque model or option ID; display text is never an ID.</summary>
public sealed record ProviderChoiceId
{
    public ProviderChoiceId(string value)
    {
        ProviderProfileRules.ValidateId(value);
        Value = value;
    }

    public string Value { get; }
}
