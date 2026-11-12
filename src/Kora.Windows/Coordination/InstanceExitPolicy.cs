using Kora.Core.Coordination;

namespace Kora.Windows.Coordination;

internal static class InstanceExitPolicy
{
    internal static void RequireRequest(InstanceMessage request, InstanceProcessIdentity owner, InstanceProcessIdentity caller)
    {
        if (request.Version != InstanceProtocol.Version || request.Kind is not "Exit"
            || request.Epoch != Guid.Empty || request.Transaction != Guid.Empty
            || request.Ticket.Length != 0 || request.Detail.Length != 0)
        {
            throw new InvalidDataException("Exit accepts no launch, task or ownership-transfer payload.");
        }
        if (!string.Equals(owner.UserSid, caller.UserSid, StringComparison.Ordinal)
            || owner.SessionId != caller.SessionId || !owner.Build.IsSameBuild(caller.Build)
            || !string.Equals(owner.Build.ExecutablePath, caller.Build.ExecutablePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Exit requires this exact Kora build and executable in the same interactive user session.");
        }
    }
}
