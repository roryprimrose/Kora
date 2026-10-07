using Kora.Core.Hosting;

namespace Kora.Core.Commands;

public sealed record SessionCommandTask(Guid Id, Guid RequestId, HostTaskState State, long Revision, RequestOrigin Origin);
