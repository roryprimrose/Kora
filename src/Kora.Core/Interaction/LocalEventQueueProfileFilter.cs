using System.Runtime.InteropServices;

using Kora.Core.Hosting;

namespace Kora.Core.Interaction;

/// <summary>Historical profile presence only, never queue admission or current readiness.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct LocalEventQueueProfileFilter(
    LocalEventQueueProfileMatch Match = LocalEventQueueProfileMatch.Any, SessionQueueReadProfile? Value = null)
{
    public bool IsValid => Match switch
    {
        LocalEventQueueProfileMatch.Any or LocalEventQueueProfileMatch.Absent => Value is null,
        LocalEventQueueProfileMatch.Exact => Value is { } value && Enum.IsDefined(value),
        _ => false,
    };
}
