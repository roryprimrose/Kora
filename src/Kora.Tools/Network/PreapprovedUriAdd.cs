using Kora.Core.Auditing;
using Kora.Core.Network;

namespace Kora.Tools.Network;

public sealed class PreapprovedUriAdd(IPreapprovedUriConfiguration configuration)
{
    internal PreapprovedUriSettings Execute(string pattern, SecurityAuditInitiator initiator) =>
        configuration.Add(pattern, initiator);
}
