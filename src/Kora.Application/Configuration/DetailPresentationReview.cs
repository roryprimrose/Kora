using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora.Application.Configuration;

public sealed class DetailPresentationReview
{
    internal DetailPresentationReview(DetailPresentationMode value, DetailPresentationMode? saved, long revision,
        WorkSessionAuthorization session, RequestOrigin origin, Func<bool> eligible, long callRevision, DateTimeOffset expires)
    {
        Value = value; Saved = saved; Revision = revision; Session = session; Origin = origin;
        Eligible = eligible; CallRevision = callRevision; Expires = expires;
    }
    public DetailPresentationMode Value { get; }
    internal DetailPresentationMode? Saved { get; }
    internal long Revision { get; }
    internal WorkSessionAuthorization Session { get; }
    internal RequestOrigin Origin { get; }
    internal Func<bool> Eligible { get; }
    internal long CallRevision { get; }
    internal DateTimeOffset Expires { get; }
    public string Description => $"Review detail presentation: {Saved?.ToString() ?? "Offer (unsaved default)"} -> {Value}. "
        + "One shared device-local preference for future publications. LinkOnly suppresses existing proactive offers only; "
        + "Open details and exact manual commands remain available. Offer preserves existing behavior, without new hands-free questions. "
        + "Confirm separately within 60 seconds or cancel. No content, speech or offers are replayed; automatic opening is unavailable.";
}
