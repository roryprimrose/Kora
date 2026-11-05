using Kora.Application.Diagnostics;
using Kora.Core.Diagnostics;

namespace Kora;

internal sealed partial class EvidenceViewModel
{
    private GitHubIssueReportWorkflow? problemReports;
    private Func<ExceptionReportSignature, GitHubExceptionReport>? prepareProblemReport;
    private long problemReportRevision;

    internal void BindProblemReporting(GitHubIssueReportWorkflow workflow,
        Func<ExceptionReportSignature, GitHubExceptionReport> prepare)
    {
        problemReports = workflow;
        prepareProblemReport = prepare;
        OnPropertyChanged(nameof(CanReportException));
    }

    public bool CanReportException => CanQuery && problemReports is not null
        && page?.Status == EvidencePageStatus.Available && selected is { ContentOmitted: false, ExceptionReport: not null };

    internal ExceptionReportViewModel PrepareExceptionReport()
    {
        if (!EnsureFindAdmission() || !CanReportException || selected?.ExceptionReport is not { } signature)
        {
            throw new InvalidOperationException("Select an available diagnostic with a retained normalised exception stack.");
        }
        var reportSource = selected;
        var snapshot = page;
        var revision = filterRevision;
        var reportRevision = problemReportRevision;
        bool Current() => !closed && !busy && canInspect() && revision == filterRevision && reportRevision == problemReportRevision
            && ReferenceEquals(reportSource, selected) && ReferenceEquals(snapshot, page);
        return new(prepareProblemReport!(signature), problemReports!, Current, logger);
    }
}
