using System.Runtime.InteropServices;

using Kora.Core.Hosting;

namespace Kora.Core.Interaction;

/// <summary>None means not applicable, never queue success, eligibility or authority.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct LocalEventQueueReasonFilter(
    LocalEventQueueReasonMatch Match = LocalEventQueueReasonMatch.Any, SessionQueueEligibility? Value = null)
{
    public bool IsValid => Match switch
    {
        LocalEventQueueReasonMatch.Any or LocalEventQueueReasonMatch.None => Value is null,
        LocalEventQueueReasonMatch.Exact => Value is { } value && Enum.IsDefined(value),
        _ => false,
    };
}
