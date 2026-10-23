namespace Kora.Core.Dependencies;

public sealed class SpeechComponentCatalogue
{
    public const int FormatVersion = 1;

    public SpeechComponentCatalogue(IEnumerable<SpeechComponentDefinition> components)
    {
        ArgumentNullException.ThrowIfNull(components);
        var entries = components.ToArray();
        if (entries.Length > 32 || entries.Any(entry => entry is null)
            || entries.Select(entry => entry.Identity).Distinct().Count() != entries.Length)
        {
            throw new ArgumentException("The host speech catalogue is bounded and identities must be unique.", nameof(components));
        }
        Components = Array.AsReadOnly(entries);
    }

    // D-002 has not selected or qualified an optional input backend. This is deliberately not an offer.
    public static SpeechComponentCatalogue Production { get; } = new([]);
    public IReadOnlyList<SpeechComponentDefinition> Components { get; }
    public SpeechComponentDefinition Resolve(SpeechComponentIdentity identity) =>
        Components.SingleOrDefault(component => component.Identity == identity)
        ?? throw new InvalidOperationException("This exact optional speech component is not in the host catalogue.");
}
