using System.Collections.Immutable;

namespace Kora;

internal sealed record NativeClipboardNode(
    int Start, int Length, NativeClipboardTag Tag, ImmutableArray<NativeClipboardNode> Children,
    long Number = 1)
{
    internal int End => Start + Length;
}
