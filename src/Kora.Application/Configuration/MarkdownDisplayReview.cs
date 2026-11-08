using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora.Application.Configuration;

public sealed class MarkdownDisplayReview
{
    internal MarkdownDisplayReview(MarkdownDisplayMode value, MarkdownDisplayMode? saved, long revision,
        WorkSessionAuthorization session, RequestOrigin origin, Func<bool> eligible, long callRevision, DateTimeOffset expires)
    {
        Value = value; Saved = saved; Revision = revision; Session = session; Origin = origin;
        Eligible = eligible; CallRevision = callRevision; Expires = expires;
    }

    public MarkdownDisplayMode Value { get; }
    internal MarkdownDisplayMode? Saved { get; }
    internal long Revision { get; }
    internal WorkSessionAuthorization Session { get; }
    internal RequestOrigin Origin { get; }
    internal Func<bool> Eligible { get; }
    internal long CallRevision { get; }
    internal DateTimeOffset Expires { get; }
    public string Description => $"Review Markdown display: {Saved?.ToString() ?? "Rendered (unsaved default)"} -> {Value}. "
        + MarkdownDisplayConfigurationService.ScopeDescription
        + " Save separately within 60 seconds or cancel. No viewer opens or changes; no source access, consent or renderer qualification is granted.";
}
