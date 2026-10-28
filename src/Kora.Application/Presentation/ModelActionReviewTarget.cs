using Kora.Core.Auditing;
using Kora.Core.Hosting;

namespace Kora.Application.Presentation;

public sealed class ModelActionReviewTarget
{
    internal ModelActionReviewTarget(HostRequest request, long epoch, SecurityAuditEvent audit, Func<bool> eligible)
    {
        Request = request;
        Epoch = epoch;
        Audit = audit;
        Eligible = eligible;
    }

    internal HostRequest Request { get; }
    internal long Epoch { get; }
    internal SecurityAuditEvent Audit { get; }
    internal Func<bool> Eligible { get; }
}
