using System.Collections.Immutable;

namespace Kora.Core.Storage;

/// <summary>A bounded passive page, not authority to reply, dispatch or change lifecycle.</summary>
public sealed record SessionPage<T>(ImmutableArray<T> Records, Guid? Next)
{
    public const int MaximumRecords = 50;
}
