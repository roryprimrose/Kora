using Kora.Core.Hosting;

namespace Kora.Application.Presentation;

internal sealed record AutomaticDetailLease(HostId<SessionIdentity> Session, HostRevision Generation,
    long SelectionEpoch, long ReaderEpoch, long InputEpoch, Func<bool> Current, Func<bool> SourceCurrent);
