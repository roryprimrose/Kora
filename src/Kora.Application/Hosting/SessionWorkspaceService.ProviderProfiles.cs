using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.Hosting;

public sealed partial class SessionWorkspaceService
{
    internal Task<T> ProviderProfileControl<T>(HostId<SessionIdentity> session, RequestOrigin origin,
        Func<bool> admission, Func<HostRequest, Func<bool>, ValueTask<T>> operation, CancellationToken token)
    {
        origin = ProviderProfileOrigin(origin);
        return ControlAsync(session, origin, operation, token, admission, existingSubject: true, resolveTerminalReceipt: true);
    }

    private static RequestOrigin ProviderProfileOrigin(RequestOrigin origin)
    {
        if (HostActivity.HasScope && HostActivity.Current is null)
        { throw new InvalidOperationException("Missing or replaced live host context cannot admit a session provider control."); }
        if (HostActivity.Current is { } current)
        {
            // Stopping an activity withdraws Activity.Current; the HasScope fence above rejects that missing segment.
            if (current.Outcome != HostOperationOutcome.Unknown || current.Request.InvocationId is not null)
            { throw new InvalidOperationException("A model/tool, stopped or completed callback cannot create an original-user session provider control."); }
            origin = current.Request.Origin;
        }
        if (origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
        { throw new InvalidOperationException("Only original local user input may inspect or review an exact session provider policy."); }
        return origin;
    }

    internal async Task<T> InspectProviderProfile<T>(HostId<SessionIdentity> session, RequestOrigin origin,
        Func<bool> admission, Func<RequestOrigin, Func<bool>, ValueTask<T>> read, CancellationToken token)
    {
        origin = ProviderProfileOrigin(origin);
        var revision = access.ControlRevision;
        bool Eligible() => access.CanInspect && access.ControlRevision == revision && admission();
        var request = new HostRequest(new(Guid.NewGuid()), session, new(Guid.NewGuid()), origin);
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        try
        {
            if (!Eligible()) { throw new InvalidOperationException("Provider inspection requires original input and current private session admission."); }
            var result = await ReadAsync(() => read(origin, Eligible), token).ConfigureAwait(false);
            if (!Eligible()) { throw new InvalidOperationException("Provider inspection changed before disclosure."); }
            activity.Complete(HostOperationOutcome.Completed);
            return result;
        }
        catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
        catch { activity.Complete(HostOperationOutcome.Failed); throw; }
    }
}
