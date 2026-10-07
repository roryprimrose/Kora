using Kora.Core.Tools;

namespace Kora.Tools.Capabilities;

internal static class ReadOnlyPage
{
    public static (int Count, int? NextOffset)? Resolve(CapabilityPageInput input, int total)
    {
        if (input.Offset >= total) { return null; }
        var count = Math.Min(input.Count, total - input.Offset);
        return (count, input.Offset + count < total ? input.Offset + count : null);
    }
}
