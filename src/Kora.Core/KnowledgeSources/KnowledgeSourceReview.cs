using System.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Core.KnowledgeSources;

public sealed record KnowledgeSourceReview(Guid ReviewId, Guid SourceId, HostRequest Request,
    KnowledgeSourceMetadata Metadata, ActivityContext Cause = default);
