using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Committed fixed-read admission and its same-run monotonic start, never a restart execution token.</summary>
public sealed record SessionQueueAdmissionReceipt(SessionQueueEntry Entry, long StartedTimestamp);
