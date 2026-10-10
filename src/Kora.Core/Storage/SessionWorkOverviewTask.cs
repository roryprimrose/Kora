using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Durable task identity/state only; bodies, diagnostic source strings and question specifications are excluded.</summary>
public sealed record SessionWorkOverviewTask(
    HostRequest Request, HostRevision Generation, HostRevision Revision, HostTaskState State, bool CurrentSource);
