using Kora.Application.Diagnostics;
using Kora.Application.Infrastructure;
using Kora.Core.Network.ExternalBrowser;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed partial class ExceptionReportViewModel(
    GitHubExceptionReport report, GitHubIssueReportWorkflow workflow, Func<bool> current, ILogger logger) : ObservableObject
{
    private readonly CancellationTokenSource lifetime = new();
    private IReadOnlyList<GitHubIssueMatch> matches = [];
    private bool checkedIssues;
    private bool busy;
    private bool closed;
    private string status = "Review the report before checking GitHub. Nothing has left this device.";

    public string ReportText => IsCurrent ? report.Title + "\n\n" + report.CreateBody(matches) : string.Empty;
    public static string Disclosure => GitHubExceptionReport.Disclosure;
    public string Status => status;
    public IReadOnlyList<GitHubIssueMatch> Matches => matches;
    public bool IsCurrent => !closed && current();
    public bool CanCheck => !busy && IsCurrent;
    public bool CanOpenNew => CanCheck && checkedIssues && matches.Count == 0;
    public bool CanOpenRegression => CanCheck && checkedIssues && matches.Count > 0 && matches.All(match => match.Closed);

    public async Task CheckAsync()
    {
        if (!CanCheck) { return; }
        busy = true;
        checkedIssues = false;
        matches = [];
        status = "Checking open and closed GitHub issues using only the fingerprint.";
        Notify();
        try
        {
            var result = await workflow.CheckAsync(report, () => IsCurrent, lifetime.Token);
            if (!IsCurrent) { return; }
            matches = result.Matches;
            checkedIssues = true;
            status = matches.Count == 0
                ? "No matching fingerprint found. Review the report, then approve browser handoff. GitHub may also suggest related issues."
                : "Matching issues found. Open an existing issue to read its status, workaround and fixing-version comments. "
                    + "Closed does not prove a fix; review the version before reporting a possible regression.";
        }
        catch (OperationCanceledException)
        {
            status = "Issue lookup cancelled or timed out. Duplicate status is unknown; no new issue was prepared.";
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure(logger, exception.GetType().Name);
            status = "Could not check GitHub (" + exception.GetType().Name
                + "). Duplicate status is unknown. Check network/local-only settings or retry explicitly; nothing was posted.";
        }
        finally
        {
            busy = false;
            if (closed) { lifetime.Dispose(); }
            Notify();
        }
    }

    public async Task OpenNewAsync()
    {
        if (!CanOpenNew && !CanOpenRegression) { return; }
        var reviewed = ReportText;
        await CheckAsync();
        if ((!CanOpenNew && !CanOpenRegression) || !string.Equals(reviewed, ReportText, StringComparison.Ordinal))
        {
            status = "The duplicate check changed or failed. Review the current result again; nothing was opened.";
            Notify();
            return;
        }
        await OpenAsync(() => report.NewIssueDestination(matches));
    }

    public Task OpenExistingAsync(GitHubIssueMatch match)
    {
        if (!CanCheck || !checkedIssues || !matches.Contains(match)) { return Task.CompletedTask; }
        return OpenAsync(() => ExternalBrowserDestination.Validate(match.Address.AbsoluteUri)
            ?? throw new InvalidDataException("The selected issue address is unavailable."));
    }

    private async Task OpenAsync(Func<ExternalBrowserDestination> destination)
    {
        ExternalBrowserDestination target;
        try { target = destination(); }
        catch (InvalidDataException)
        {
            status = "Browser handoff unavailable: the complete report or issue address exceeds admission limits. No truncation and nothing was opened.";
            Notify();
            return;
        }
        busy = true;
        Notify();
        try
        {
            var outcome = await workflow.OpenAsync(target, () => IsCurrent, lifetime.Token);
            status = outcome switch
            {
                ExternalBrowserLaunchOutcome.Accepted => "Windows accepted the GitHub browser handoff. No issue submission is verified. "
                    + "Sign in, review or edit the report, then select Create on GitHub to post.",
                ExternalBrowserLaunchOutcome.Denied => "Browser handoff denied by current review, privacy or network policy. Nothing was posted by Kora.",
                ExternalBrowserLaunchOutcome.Cancelled => "Browser handoff cancelled. No issue submission is verified.",
                ExternalBrowserLaunchOutcome.Failed => "Browser handoff failed. No issue submission is verified.",
                _ => "Browser handoff outcome unknown. It may have opened; do not retry without checking the browser. No issue submission is verified.",
            };
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Failure(logger, exception.GetType().Name);
            status = "Browser handoff unconfirmed (" + exception.GetType().Name
                + "). It may have opened; inspect the browser before retrying. No issue submission is verified.";
        }
        finally
        {
            busy = false;
            if (closed) { lifetime.Dispose(); }
            Notify();
        }
    }

    public void Close()
    {
        if (closed) { return; }
        closed = true;
        lifetime.Cancel();
        if (!busy) { lifetime.Dispose(); }
        matches = [];
        checkedIssues = false;
        Notify();
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(ReportText));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Matches));
        OnPropertyChanged(nameof(CanCheck));
        OnPropertyChanged(nameof(CanOpenNew));
        OnPropertyChanged(nameof(CanOpenRegression));
    }
}
