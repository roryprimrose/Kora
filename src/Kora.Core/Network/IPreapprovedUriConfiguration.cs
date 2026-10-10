using Kora.Core.Auditing;

namespace Kora.Core.Network;

public interface IPreapprovedUriConfiguration
{
    PreapprovedUriSettings GetSettings();

    PreapprovedUriSettings Add(string pattern, SecurityAuditInitiator initiator);

    PreapprovedUriSettings Remove(string pattern, SecurityAuditInitiator initiator);

    PreapprovedUriSettings Clear(SecurityAuditInitiator initiator);

    bool IsPreapproved(Uri uri);
}
