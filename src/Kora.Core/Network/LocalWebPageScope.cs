using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Kora.Core.Network;

/// <summary>Frozen original-user destinations for one bounded local web workflow, not action permission.</summary>
public sealed class LocalWebPageScope
{
    [JsonConstructor]
    public LocalWebPageScope(ImmutableArray<Uri> destinations)
    {
        if (destinations.IsDefaultOrEmpty || destinations.Length > 2)
        { throw new InvalidDataException("Select one or two explicit page destinations."); }
        foreach (var destination in destinations) { _ = WebPageAccessBinding.DestinationDigest(destination); }
        if (destinations.Distinct().Count() != destinations.Length)
        { throw new InvalidDataException("Each page destination must be distinct; equal URLs are not two sources."); }
        Destinations = destinations;
    }

    public ImmutableArray<Uri> Destinations { get; }
    [JsonIgnore]
    public int MaximumCalls => Destinations.Length;
    [JsonIgnore]
    public int MaximumRounds => MaximumCalls + 1;
}
