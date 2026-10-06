using System.Collections.Immutable;

namespace Kora.Application.Interaction;

internal static class InteractionArrayExtensions
{
    public static int FindIndex<T>(this ImmutableArray<T> values, Func<T, bool> predicate)
    {
        for (var index = 0; index < values.Length; index++)
        {
            if (predicate(values[index]))
            {
                return index;
            }
        }
        return -1;
    }
}
