using Kora.Core.Auditing;
using Kora.Core.Network;

namespace Kora.Tools.Network;

public sealed class PreapprovedUriRemove(IPreapprovedUriConfiguration configuration)
{
    internal PreapprovedUriSettings Execute(string pattern, SecurityAuditInitiator initiator) =>
        configuration.Remove(pattern, initiator);
}
