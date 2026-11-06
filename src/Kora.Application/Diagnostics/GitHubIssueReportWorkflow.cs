using Kora.Application.Network.ExternalBrowser;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Network.ExternalBrowser;

namespace Kora.Application.Diagnostics;

public sealed class GitHubIssueReportWorkflow(
    IGitHubIssueLookup issues, IExternalBrowserNetworkPolicy network, ExternalBrowserWorkflow browser,
    ISecurityAuditLog audit, TimeProvider time)
{
    public async Task<GitHubIssueLookupResult> CheckAsync(
        GitHubExceptionReport report, Func<bool> current, CancellationToken cancellationToken)
    {
        var host = HostActivity.RequireCurrent();
        if (host.Activity!.IsStopped || host.Outcome != HostOperationOutcome.Unknown || host.Request.Origin != RequestOrigin.LocalUi)
        {
            throw new InvalidOperationException("Issue lookup requires original native user input.");
        }
        var admission = network.Capture();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30), time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        bool Live() => !linked.IsCancellationRequested && current() && admission.Allowed && admission.Current();
        var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
            "diagnostics.lookup-github-issue", SecurityAuditOutcome.Requested,
            SecurityAuditInitiator.LocalUser, "diagnostics.github-issues");
        using var activity = HostActivity.BeginAudit(host.Request, requested);
        audit.Write(requested);
        var outcome = SecurityAuditOutcome.Failed;
        try
        {
            if (!Live()) { throw new InvalidOperationException("Issue lookup denied by host privacy, offline or local-only mode."); }
            var result = await issues.FindAsync(report.Signature.Fingerprint, Live, linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            if (!Live()) { throw new InvalidOperationException("Issue lookup was retired; no result can be presented."); }
            outcome = SecurityAuditOutcome.Succeeded;
            return result;
        }
        catch (OperationCanceledException)
        {
            outcome = SecurityAuditOutcome.Cancelled;
            throw;
        }
        finally
        {
            audit.Write(requested.WithOutcome(outcome));
            activity.Complete(outcome == SecurityAuditOutcome.Succeeded ? HostOperationOutcome.Completed
                : outcome == SecurityAuditOutcome.Cancelled ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        }
    }

    public Task<ExternalBrowserLaunchOutcome> OpenAsync(
        ExternalBrowserDestination destination, Func<bool> current, CancellationToken cancellationToken)
    {
        var review = browser.PrepareIssue(destination, current);
        return browser.ConfirmAsync(review.Id, current, cancellationToken);
    }
}
