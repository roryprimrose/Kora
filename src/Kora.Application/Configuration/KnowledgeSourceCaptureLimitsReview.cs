using Kora.Core.KnowledgeSources;

namespace Kora.Application.Configuration;

public sealed class KnowledgeSourceCaptureLimitsReview
{
    internal KnowledgeSourceCaptureLimitsReview(KnowledgeSource source, KnowledgeSourceCaptureLimits? saved,
        KnowledgeSourceCaptureLimits? proposed, DateTimeOffset expires, Func<bool> eligible)
    { Source = source; Saved = saved; Proposed = proposed; Expires = expires; Eligible = eligible; }

    public KnowledgeSource Source { get; }
    public KnowledgeSourceCaptureLimits? Saved { get; }
    public KnowledgeSourceCaptureLimits? Proposed { get; }
    public DateTimeOffset Expires { get; }
    internal Func<bool> Eligible { get; }
}
