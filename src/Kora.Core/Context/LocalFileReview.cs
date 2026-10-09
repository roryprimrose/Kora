using System.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Core.Context;

public sealed record LocalFileReview(Guid ReviewId, Guid SourceId, HostRequest Request, LocalFileMetadata Metadata,
    ActivityContext Cause = default)
{
    public LocalFileMetadata? PreviousMetadata { get; init; }
}
