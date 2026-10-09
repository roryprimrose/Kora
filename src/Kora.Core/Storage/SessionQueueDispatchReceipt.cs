using Kora.Core.Hosting;
using Kora.Core.Tools;

namespace Kora.Core.Storage;

public sealed record SessionQueueDispatchReceipt(SessionQueueEntry Entry, ApplicationVersionObservation? Version);
