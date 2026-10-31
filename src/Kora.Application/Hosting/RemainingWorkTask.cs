using Kora.Core.Hosting;

namespace Kora.Application.Hosting;

public sealed record RemainingWorkTask(HostRequest Request, HostRevision Generation, HostRevision Revision,
    HostTaskState State, bool CurrentSource);