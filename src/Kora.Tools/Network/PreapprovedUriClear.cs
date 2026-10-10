using Kora.Core.Auditing;
using Kora.Core.Network;

namespace Kora.Tools.Network;

public sealed class PreapprovedUriClear(IPreapprovedUriConfiguration configuration)
{
    internal PreapprovedUriSettings Execute(SecurityAuditInitiator initiator) =>
        configuration.Clear(initiator);
}
