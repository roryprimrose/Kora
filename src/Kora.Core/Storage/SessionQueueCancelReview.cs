using System.Collections.Immutable;

using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Host-issued complete reviewed subset in current dispatch order.</summary>
public sealed record SessionQueueCancelReview(Guid Id, SessionQueueSnapshot Queue,
    ImmutableArray<SessionQueueEntry> Entries, long AdmissionRevision, DateTimeOffset ObservedAt);
