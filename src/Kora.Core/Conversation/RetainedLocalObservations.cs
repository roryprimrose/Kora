using System.Collections.Immutable;

using Kora.Core.Dependencies;

namespace Kora.Core.Conversation;

public sealed record RetainedLocalObservations(int Version, ImmutableArray<RetainedLocalObservation> Items)
{
    public void Validate()
    {
        if (Version != 1 || Items.IsDefaultOrEmpty || Items.Length > ModelToolProtocol.MaximumCalls
            || Items.Any(item => item is null) || Items.Select(item => item.Id).Distinct().Count() != Items.Length)
        { throw new InvalidDataException("The versioned retained observation collection is invalid or oversized."); }
        foreach (var item in Items) { item.Validate(); }
    }

    public bool Equals(RetainedLocalObservations? other) =>
        other is not null && Version == other.Version && Items.SequenceEqual(other.Items);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Version);
        foreach (var item in Items) { hash.Add(item); }
        return hash.ToHashCode();
    }
}
