using System.Diagnostics;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Application.Network.ExternalBrowser;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Network.ExternalBrowser;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection(nameof(HeadlessUiTestGroup))]
public sealed class ExceptionReportTests
{
    [Fact]
    public async Task Native_review_requires_lookup_approval_then_browser_approval_and_never_posts()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            var window = new ExceptionReportWindow(f.Model);
            window.Show();
            try
            {
                var preview = window.GetVisualDescendants().OfType<TextBox>().Single();
                preview.Text.Should().Contain(f.Report.Signature.StackTrace).And.Contain(f.Report.Signature.Fingerprint);
                f.Lookup.Calls.Should().Be(0);
                f.Launcher.Calls.Should().Be(0);
                f.Model.CanOpenNew.Should().BeFalse();
                Click(window, "Check GitHub using this fingerprint");
                f.Lookup.Calls.Should().Be(1);
                f.Model.CanOpenNew.Should().BeTrue();
                f.Launcher.Calls.Should().Be(0);
                Click(window, "Approve report and review new issue on GitHub");
                f.Lookup.Calls.Should().Be(2, "browser handoff must recheck duplicate status");
                f.Launcher.Calls.Should().Be(1);
                f.Launcher.Address.Should().StartWith("https://github.com/roryprimrose/Kora/issues/new?");
                Uri.UnescapeDataString(f.Launcher.Address!).Should().Contain(f.Report.Body);
                f.Model.Status.Should().Contain("No issue submission is verified").And.Contain("Create on GitHub");
            }
            finally { window.Close(); }
            f.Model.ReportText.Should().BeEmpty();
        });
    }

    [Fact]
    public async Task Existing_open_issue_suppresses_new_post_and_can_be_opened_without_a_GitHub_account()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            var match = new GitHubIssueMatch(12, "Existing problem", false);
            f.Lookup.Result = new([match]);
            using var root = Root();
            await f.Model.CheckAsync();
            f.Model.Matches.Should().Equal(match);
            f.Model.CanOpenNew.Should().BeFalse();
            f.Model.CanOpenRegression.Should().BeFalse();
            await f.Model.OpenNewAsync();
            f.Launcher.Calls.Should().Be(0);
            await f.Model.OpenExistingAsync(new(999, "foreign", false));
            f.Launcher.Calls.Should().Be(0);
            await f.Model.OpenExistingAsync(match);
            f.Launcher.Address.Should().Be("https://github.com/roryprimrose/Kora/issues/12");
            f.Lookup.Calls.Should().Be(1);
            f.Model.Close();
        });
    }

    [Fact]
    public async Task Closed_match_offers_explicit_regression_review_with_prior_issue_link()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            f.Lookup.Result = new([new(12, "Closed problem", true)]);
            using var root = Root();
            await f.Model.CheckAsync();
            f.Model.CanOpenNew.Should().BeFalse();
            f.Model.CanOpenRegression.Should().BeTrue();
            f.Model.ReportText.Should().Contain("#12").And.Contain("Verify that this version includes");
            f.Model.Status.Should().Contain("fixing-version").And.Contain("Closed does not prove a fix");
            await f.Model.OpenNewAsync();
            Uri.UnescapeDataString(f.Launcher.Address!).Should().Contain("## Possible regression").And.Contain("#12");
            f.Model.Close();
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Newly_discovered_duplicate_or_failed_recheck_prevents_browser_handoff(bool duplicate)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            using var root = Root();
            await f.Model.CheckAsync();
            if (duplicate) { f.Lookup.Result = new([new(14, "New duplicate", false)]); }
            else { f.Lookup.Failure = new HttpRequestException("private transport text"); }
            await f.Model.OpenNewAsync();
            f.Launcher.Calls.Should().Be(0);
            f.Model.Status.Should().Contain("duplicate check changed or failed");
            f.Model.Status.Should().NotContain("private transport text");
            f.Model.Close();
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Privacy_or_close_during_ignoring_lookup_never_presents_late_content(bool close)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            using var root = Root();
            var release = new TaskCompletionSource<GitHubIssueLookupResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Lookup.Hold = release.Task;
            var pending = f.Model.CheckAsync();
            f.Model.CanCheck.Should().BeFalse();
            await f.Model.CheckAsync();
            f.Lookup.Calls.Should().Be(1);
            if (close) { f.Model.Close(); } else { f.Current = false; }
            release.SetResult(new([new(12, "private late issue", false)]));
            await pending;
            f.Model.Matches.Should().BeEmpty();
            f.Model.ReportText.Should().BeEmpty();
            f.Model.CanOpenNew.Should().BeFalse();
            f.Launcher.Calls.Should().Be(0);
            f.Model.Close();
            f.Model.Close();
        });
    }

    [Theory]
    [InlineData(ExternalBrowserLaunchOutcome.Accepted, "No issue submission is verified")]
    [InlineData(ExternalBrowserLaunchOutcome.Failed, "failed")]
    [InlineData(ExternalBrowserLaunchOutcome.Denied, "denied")]
    [InlineData(ExternalBrowserLaunchOutcome.Cancelled, "cancelled")]
    [InlineData(ExternalBrowserLaunchOutcome.Unknown, "unknown")]
    public async Task Browser_outcomes_never_claim_issue_creation(ExternalBrowserLaunchOutcome outcome, string expected)
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            using var root = Root();
            f.Launcher.Outcome = outcome;
            await f.Model.CheckAsync();
            await f.Model.OpenNewAsync();
            f.Model.Status.Should().Contain(expected).And.NotContain("issue created");
            f.Launcher.Calls.Should().Be(1);
            f.Model.Close();
        });
    }

    [Fact]
    public async Task Selected_evidence_record_is_required_and_source_edits_retire_the_review()
    {
        await HeadlessSession.RunAsync(async () =>
        {
            using var f = new Fixture();
            var reader = new Reader(f.Report.Signature);
            var model = new EvidenceViewModel(new(reader, new Access(), TimeProvider.System,
                NullLogger<DurableEvidenceQuery>.Instance), () => f.Current, NullLogger<EvidenceViewModel>.Instance);
            model.BindProblemReporting(f.Workflow, signature => new(signature, "1.2.3", "10.0.26100", "X64"));
            model.CanReportException.Should().BeFalse();
            FluentActions.Invoking(() => model.PrepareExceptionReport()).Should().Throw<InvalidOperationException>();
            await model.SearchAsync();
            model.Select(model.Records.Single());
            model.CanReportException.Should().BeTrue();
            var report = model.PrepareExceptionReport();
            report.ReportText.Should().Contain(reader.Record.ExceptionReport!.StackTrace);
            var original = model.Records.Single();
            model.Select(null);
            model.Select(original);
            report.IsCurrent.Should().BeFalse("selection changes cannot revive an earlier review");
            report.Close();
            report = model.PrepareExceptionReport();
            model.SafeText = "changed";
            report.IsCurrent.Should().BeFalse();
            report.ReportText.Should().BeEmpty();
            using var root = Root();
            await report.CheckAsync();
            f.Lookup.Calls.Should().Be(0);
            report.Close();
            model.Close();
        });
    }

    private static void Click(Window window, string content) =>
        window.GetVisualDescendants().OfType<Button>().Single(button => string.Equals(button.Content as string, content, StringComparison.Ordinal))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static HostActivity Root() => HostActivity.BeginRoot(
        HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Evidence);

    private sealed class Fixture : IDisposable
    {
        private readonly ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        public Fixture()
        {
            ActivitySource.AddActivityListener(listener);
            var network = new Network();
            var audit = new Audit();
            Workflow = new(Lookup, network, new(Launcher, network, audit, TimeProvider.System), audit, TimeProvider.System);
            Report = new(ExceptionReportSignature.Create("System.InvalidOperationException", "Tests.Example::Operation()"),
                "1.2.3", "10.0.26100", "X64");
            Model = new(Report, Workflow, () => Current, NullLogger.Instance);
        }
        public bool Current { get; set; } = true;
        public Lookup Lookup { get; } = new();
        public Launcher Launcher { get; } = new();
        public GitHubExceptionReport Report { get; }
        public GitHubIssueReportWorkflow Workflow { get; }
        public ExceptionReportViewModel Model { get; }
        public void Dispose() { Model.Close(); listener.Dispose(); }
    }

    private sealed class Lookup : IGitHubIssueLookup
    {
        public int Calls { get; private set; }
        public GitHubIssueLookupResult Result { get; set; } = new([]);
        public Exception? Failure { get; set; }
        public Task<GitHubIssueLookupResult>? Hold { get; set; }
        public Task<GitHubIssueLookupResult> FindAsync(string fingerprint, Func<bool> admitted, CancellationToken cancellationToken)
        {
            Calls++;
            if (Failure is not null) { return Task.FromException<GitHubIssueLookupResult>(Failure); }
            return Hold ?? Task.FromResult(Result);
        }
    }

    private sealed class Launcher : IExternalBrowserLauncher
    {
        public int Calls { get; private set; }
        public string? Address { get; private set; }
        public ExternalBrowserLaunchOutcome Outcome { get; set; } = ExternalBrowserLaunchOutcome.Accepted;
        public Task<ExternalBrowserLaunchOutcome> LaunchAsync(ExternalBrowserDestination destination, Func<bool> admitted, CancellationToken cancellationToken)
        {
            Calls++;
            Address = destination.ExactAddress;
            return Task.FromResult(Outcome);
        }
    }

    private sealed class Network : IExternalBrowserNetworkPolicy
    {
        public ExternalBrowserNetworkAdmission Capture() => new(true, static () => true);
    }

    private sealed class Audit : ISecurityAuditLog { public void Write(SecurityAuditEvent auditEvent) { } }
    private sealed class Access : IEvidenceQueryAccess { public bool CanInspect => true; }
    private sealed class Reader(ExceptionReportSignature signature) : IEvidenceReader
    {
        public EvidenceRecord Record { get; } = new(new(EvidenceSource.Log, new(Guid.NewGuid())),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1), EvidenceSegmentStatus.Present, null, null, null, null,
            "Error", "Test", 1, "Safe operation failed", new Dictionary<string, EvidenceValue>(StringComparer.Ordinal),
            null, null, [], ExceptionType: signature.ExceptionType, ExceptionReport: signature);
        public ValueTask<EvidenceReadBatch> ReadAsync(EvidenceQuery query, EvidenceReadCheckpoint? checkpoint,
            HostRequest request, DateTimeOffset now, CancellationToken cancellationToken) => new(new EvidenceReadBatch(
                new(1, 0, 0, 0), [new(new(1, EvidenceSource.Log, Record.Reference.Id.Value.ToString("D"), 0), Record)],
                null, false, false));
    }
}
