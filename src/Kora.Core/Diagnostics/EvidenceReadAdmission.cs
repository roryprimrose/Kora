using Kora.Core.Hosting;

namespace Kora.Core.Diagnostics;

/// <summary>Original user-channel admission for private evidence reads; never model or effect authority.</summary>
public static class EvidenceReadAdmission
{
    /// <summary>Allows native inspection, or uninvoked activated-voice reads of committed authority audit only.</summary>
    public static bool IsAllowed(HostRequest request, EvidenceSource source)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.InvocationId is null && (request.Origin == RequestOrigin.LocalUi
            || request.Origin == RequestOrigin.ActivatedVoice && source == EvidenceSource.AuthorityAudit);
    }
}
