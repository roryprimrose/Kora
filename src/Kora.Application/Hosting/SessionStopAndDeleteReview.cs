using System.Diagnostics;

using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

/// <summary>A single-use host-held review, never a reusable deletion capability.</summary>
public sealed class SessionStopAndDeleteReview
{
    internal SessionStopAndDeleteReview(SessionDispositionPreview disposition, RequestOrigin origin,
        long controlRevision, DateTimeOffset createdAt, DateTimeOffset expiresAt, SessionStopAndDeletePhase phase,
        ActivityLink workLink = default)
    {
        Disposition = disposition;
        Origin = origin;
        ControlRevision = controlRevision;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        Phase = phase;
        WorkLink = workLink;
    }

    public SessionDispositionPreview Disposition { get; }
    public RequestOrigin Origin { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset ExpiresAt { get; }
    public SessionStopAndDeletePhase Phase { get; }
    internal long ControlRevision { get; }
    internal ActivityLink WorkLink { get; }
}
