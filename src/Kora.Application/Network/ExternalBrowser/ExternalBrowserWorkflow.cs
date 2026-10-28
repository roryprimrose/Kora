using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Network.ExternalBrowser;
using Kora.Core.Presentation;
using Kora.Application.Presentation;

namespace Kora.Application.Network.ExternalBrowser;

public sealed class ExternalBrowserWorkflow(
    IExternalBrowserLauncher launcher, IExternalBrowserNetworkPolicy network, ISecurityAuditLog audit, TimeProvider time)
{
    private readonly Lock gate = new();
    private ExternalBrowserReview? pending;
    private long generation;
    private bool active;
    private Task quiescence = Task.CompletedTask;

    public Task RetireAndWaitAsync()
    {
        lock (gate)
        {
            pending?.FinalSource?.Retire();
            pending = null;
            generation++;
#pragma warning disable VSTHRD003 // The launch owner signals an asynchronous TCS; exit yields until that separately owned handshake terminates.
            return quiescence;
#pragma warning restore VSTHRD003
        }
    }

    public ExternalBrowserReview Prepare(WebResultSnapshot snapshot, Func<bool> current)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(current);
        RequireNativeUser();
        lock (gate)
        {
            pending?.FinalSource?.Retire();
            pending = null;
            generation++;
            if (active || !current()) { throw new InvalidOperationException("The exact web-result source or host is unavailable."); }
            var (destination, admission) = AdmitDestination(snapshot.Provenance.FinalAddress);
            return pending = new(snapshot, destination, current, admission, time.GetUtcNow().AddMinutes(2));
        }
    }

    public ExternalBrowserReview Prepare(FinalDocumentNavigationRequest request, Func<bool> current)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(current);
        RequireNativeUser();
        lock (gate)
        {
            pending?.FinalSource?.Retire();
            pending = null;
            generation++;
            if (!request.TryConsume(HostActivity.RequireCurrent().Request) || active
                || !request.IsCurrent || !current())
            { throw new InvalidOperationException("The exact final source or original native action is no longer available."); }
            var (destination, admission) = AdmitDestination(request.Destination);
            return pending = new(destination, () => request.IsCurrent && current(), admission,
                time.GetUtcNow().AddMinutes(2), request);
        }
    }

    public bool IsCurrent(ExternalBrowserReview review)
    {
        lock (gate)
        {
            return ReferenceEquals(pending, review) && time.GetUtcNow() < review.ExpiresAt
                && review.Current() && review.Network.Current();
        }
    }

    public void Retire()
    {
        lock (gate) { pending?.FinalSource?.Retire(); pending = null; generation++; }
    }

    public void Retire(Guid reviewId)
    {
        lock (gate)
        {
            if (pending?.Id != reviewId) { return; }
            pending.FinalSource?.Retire();
            pending = null;
            generation++;
        }
    }

    private (ExternalBrowserDestination Destination, ExternalBrowserNetworkAdmission Admission) AdmitDestination(string address)
    {
        var destination = ExternalBrowserDestination.Validate(address)
            ?? throw new InvalidOperationException("External browsing requires a validated public HTTPS destination without credentials or a fragment.");
        var admission = network.Capture();
        if (!admission.Allowed || !admission.Current())
        { throw new InvalidOperationException("External browsing is unavailable offline or in local-only/unknown mode. No mode was changed."); }
        return (destination, admission);
    }

    public async Task<ExternalBrowserLaunchOutcome> ConfirmAsync(
        Guid reviewId, Func<bool> current, CancellationToken token)
    {
        RequireNativeUser();
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Tool);
        ExternalBrowserReview? review;
        long admittedGeneration;
        TaskCompletionSource? released = null;
        lock (gate)
        {
            review = pending?.Id == reviewId && !active ? pending : null;
            if (pending is { } rejected && !ReferenceEquals(review, rejected)) { rejected.FinalSource?.Retire(); }
            pending = null;
            admittedGeneration = generation;
            if (review is not null)
            {
                active = true;
                released = new(TaskCreationOptions.RunContinuationsAsynchronously);
                quiescence = released.Task;
            }
        }
        var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
            "network.open-external-browser", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser, "network.external-browser");
        var requestedWritten = false;
        var terminalAttempted = false;
        try
        {
            audit.Write(requested);
            requestedWritten = true;
            bool Live() => review is not null && !token.IsCancellationRequested && current()
                && Interlocked.Read(ref generation) == admittedGeneration && time.GetUtcNow() < review.ExpiresAt
                && review.Current() && review.Network.Current();
            var sourceValid = true;
            if (review?.FinalSource is { } final && Live())
            {
                try { sourceValid = await final.Source.ValidateAsync(token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { sourceValid = false; }
            }
            var result = token.IsCancellationRequested ? ExternalBrowserLaunchOutcome.Cancelled
                : !sourceValid || !Live() ? ExternalBrowserLaunchOutcome.Denied
                : await launcher.LaunchAsync(review!.Destination, Live, token);
            // Revocation cannot undo a request already handed to another application's lifetime.
            if (result == ExternalBrowserLaunchOutcome.Accepted && !Live()) { result = ExternalBrowserLaunchOutcome.Unknown; }
            terminalAttempted = true;
            audit.Write(requested.WithOutcome(result switch
            {
                ExternalBrowserLaunchOutcome.Accepted => SecurityAuditOutcome.Succeeded,
                ExternalBrowserLaunchOutcome.Cancelled => SecurityAuditOutcome.Cancelled,
                ExternalBrowserLaunchOutcome.Denied => SecurityAuditOutcome.Denied,
                ExternalBrowserLaunchOutcome.Failed => SecurityAuditOutcome.Failed,
                _ => SecurityAuditOutcome.Unknown,
            }, result.ToString().ToLowerInvariant()));
            activity.Complete(result == ExternalBrowserLaunchOutcome.Accepted ? HostOperationOutcome.Completed
                : result == ExternalBrowserLaunchOutcome.Cancelled ? HostOperationOutcome.Cancelled
                : result == ExternalBrowserLaunchOutcome.Unknown ? HostOperationOutcome.Unknown : HostOperationOutcome.Failed);
            return result;
        }
        catch
        {
            // No successful receipt can be inferred after a launch/audit exception.
            activity.Complete(HostOperationOutcome.Unknown);
            if (requestedWritten && !terminalAttempted)
            { audit.Write(requested.WithOutcome(SecurityAuditOutcome.Unknown, "launch-unconfirmed")); }
            throw;
        }
        finally
        {
            if (review is not null)
            {
                review.FinalSource?.Retire();
                lock (gate) { active = false; }
                released!.SetResult();
            }
        }
    }

    private static void RequireNativeUser()
    {
        var host = HostActivity.RequireCurrent();
        if (host.Activity!.IsStopped || host.Outcome != HostOperationOutcome.Unknown || host.Request.Origin != RequestOrigin.LocalUi)
        { throw new InvalidOperationException("External-browser privacy review requires live native user input; content cannot launch a browser."); }
    }
}
