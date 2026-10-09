using System.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Core.Context;

public sealed record LocalFolderReview(Guid ReviewId, Guid SourceId, HostRequest Request, LocalFolderMetadata Metadata,
    ActivityContext Cause = default);
