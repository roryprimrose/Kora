using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>An exact displayed pending identity and revision, never a positional or executable selector.</summary>
public sealed record SessionQueueSelection(HostId<TaskIdentity> TaskId, HostRevision Revision);
