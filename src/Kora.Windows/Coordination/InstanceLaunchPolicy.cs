namespace Kora.Windows.Coordination;

internal static class InstanceLaunchPolicy
{
#if DEBUG
    internal const string RequestKind = "Start";
    internal static bool AcceptsActivation(bool sameBuild) => sameBuild;
#else
    internal const string RequestKind = "Activate";
    internal static bool AcceptsActivation(bool sameBuild) => true;
#endif

    internal static bool RequiresActivation(InstanceMessage request, bool sameBuild, bool incomingDebug)
    {
        if (request.Kind is not ("Start" or "Activate") || request.Epoch != Guid.Empty
            || request.Transaction != Guid.Empty || request.Ticket.Length != 0)
        {
            throw new InvalidDataException("Unsupported or stale lifecycle request.");
        }
        return string.Equals(request.Kind, "Activate", StringComparison.Ordinal) || !incomingDebug || sameBuild;
    }
}
