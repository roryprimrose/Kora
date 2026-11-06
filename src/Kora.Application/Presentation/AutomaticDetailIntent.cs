using Kora.Application.Configuration;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora.Application.Presentation;

internal sealed class AutomaticDetailIntent(HostRequest request, AdmittedDetailContent content,
    DetailPresentationConfigurationService preference, AutomaticDetailPreference preferenceEpoch, AutomaticDetailLease lease, Func<bool> sourceCurrent)
{
    private int consumed;
    internal HostRequest Request { get; } = request;
    internal AdmittedDetailContent Content { get; } = content;
    internal AutomaticDetailPreference PreferenceEpoch { get; } = preferenceEpoch;
    internal AutomaticDetailLease Lease { get; } = lease;

    internal bool IsCurrent() => preference.IsAutomaticCurrent(PreferenceEpoch) && Lease.Current() && IsSourceCurrent();

    internal bool IsSourceCurrent() => Lease.SourceCurrent() && sourceCurrent()
        && Request.SessionId == Lease.Session
        && Content.Origin == DetailContentOrigin.FinalizedResponse
        && Content.SessionSource!.SessionId == Request.SessionId
        && Content.SessionSource.RequestId == Request.RequestId && Content.SessionSource.TaskId == Request.TaskId;

    internal bool TryConsume() => Interlocked.Exchange(ref consumed, 1) == 0 && IsCurrent();
}
