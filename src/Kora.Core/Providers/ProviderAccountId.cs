namespace Kora.Core.Providers;

/// <summary>An opaque host-owned account binding, never a credential or sign-in authority.</summary>
public sealed record ProviderAccountId
{
    public ProviderAccountId(string value)
    {
        ProviderProfileRules.ValidateId(value);
        Value = value;
    }

    public string Value { get; }
}
