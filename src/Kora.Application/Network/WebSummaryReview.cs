using System.Diagnostics;
using Kora.Core.Dependencies;
using Kora.Core.Presentation;

namespace Kora.Application.Network;

/// <summary>One-use exact-source disclosure review; its identity is not network or provider authority.</summary>
public sealed class WebSummaryReview
{
    internal WebSummaryReview(WebResultSnapshot snapshot, Func<bool> current, DateTimeOffset expiresAt,
        LocalContextSessionTarget target, ActivityContext trace)
    {
        Snapshot = snapshot;
        Current = current;
        ExpiresAt = expiresAt;
        Target = target;
        Trace = trace;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public DetailContentReference Reference => Snapshot.Reference;
    public DateTimeOffset ExpiresAt { get; }
    public Guid SessionId => Target.Request.SessionId.Value;
    internal WebResultSnapshot Snapshot { get; }
    internal Func<bool> Current { get; }
    internal LocalContextSessionTarget Target { get; }
    internal ActivityContext Trace { get; }
}
