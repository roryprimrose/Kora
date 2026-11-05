using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Application.Network.ExternalBrowser;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Network.ExternalBrowser;

namespace Kora.Application.UnitTests.Diagnostics;

[Collection("Host tracing")]
public sealed class GitHubIssueReportWorkflowTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public GitHubIssueReportWorkflowTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Fact]
    public async Task Original_user_lookup_has_metadata_only_correlated_request_and_terminal_audits()
    {
        using var root = Root();
        var lookup = new Lookup();
        var audit = new Audit();
        var workflow = Create(lookup, audit: audit);
        var result = await workflow.CheckAsync(GitHubExceptionReportTests.Create(), static () => true, Token);
        result.Matches.Should().BeEmpty();
        lookup.Fingerprint.Should().Be(GitHubExceptionReportTests.Signature().Fingerprint);
        lookup.Calls.Should().Be(1);
        audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        audit.Events.Select(item => item.CorrelationId).Distinct().Should().ContainSingle();
        audit.Events.Should().OnlyContain(item => item.ActionId == "diagnostics.lookup-github-issue"
            && item.TargetId == "diagnostics.github-issues");
    }

    [Theory]
    [InlineData(RequestOrigin.HostSystem)]
    [InlineData(RequestOrigin.ActivatedVoice)]
    public async Task Content_or_system_origins_have_no_lookup_authority(RequestOrigin origin)
    {
        using var root = Root(origin);
        var lookup = new Lookup();
        await FluentActions.Awaiting(() => Create(lookup).CheckAsync(GitHubExceptionReportTests.Create(), static () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        lookup.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Completed_native_context_cannot_start_lookup()
    {
        using var root = Root();
        root.Complete(HostOperationOutcome.Completed);
        var lookup = new Lookup();
        await FluentActions.Awaiting(() => Create(lookup).CheckAsync(GitHubExceptionReportTests.Create(), static () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        lookup.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("network")]
    [InlineData("policy")]
    public async Task Denied_admission_performs_no_request_and_records_failure(string reason)
    {
        using var root = Root();
        var lookup = new Lookup();
        var audit = new Audit();
        var network = new Network
        {
            Allowed = !string.Equals(reason, "policy", StringComparison.Ordinal),
            Current = !string.Equals(reason, "network", StringComparison.Ordinal),
        };
        await FluentActions.Awaiting(() => Create(lookup, network, audit).CheckAsync(GitHubExceptionReportTests.Create(),
            () => !string.Equals(reason, "source", StringComparison.Ordinal), Token)).Should().ThrowAsync<InvalidOperationException>();
        lookup.Calls.Should().Be(0);
        audit.Events[^1].Outcome.Should().Be(SecurityAuditOutcome.Failed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Revocation_or_cancellation_during_lookup_suppresses_late_result(bool cancel)
    {
        using var root = Root();
        var release = new TaskCompletionSource<GitHubIssueLookupResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lookup = new Lookup { Response = release.Task };
        var audit = new Audit();
        var source = true;
        using var cancellation = new CancellationTokenSource();
        var pending = Create(lookup, audit: audit).CheckAsync(GitHubExceptionReportTests.Create(), () => source, cancellation.Token);
        if (cancel) { await cancellation.CancelAsync(); } else { source = false; }
        release.SetResult(new([]));
        if (cancel)
        {
            await FluentActions.Awaiting(() => pending).Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            await FluentActions.Awaiting(() => pending).Should().ThrowAsync<InvalidOperationException>();
        }
        audit.Events[^1].Outcome.Should().Be(cancel ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Failed);
    }

    [Fact]
    public async Task Transport_or_audit_failure_never_becomes_a_success()
    {
        using var root = Root();
        var audit = new Audit();
        var lookup = new Lookup { Response = Task.FromException<GitHubIssueLookupResult>(new IOException("not logged")) };
        await FluentActions.Awaiting(() => Create(lookup, audit: audit).CheckAsync(GitHubExceptionReportTests.Create(), static () => true, Token))
            .Should().ThrowAsync<IOException>();
        audit.Events[^1].Outcome.Should().Be(SecurityAuditOutcome.Failed);
        audit.Fail = true;
        await FluentActions.Awaiting(() => Create(lookup, audit: audit).CheckAsync(GitHubExceptionReportTests.Create(), static () => true, Token))
            .Should().ThrowAsync<IOException>();
        lookup.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Browser_handoff_reuses_native_network_admission_and_audit_without_posting()
    {
        using var root = Root();
        var launcher = new Launcher();
        var audit = new Audit();
        var workflow = Create(new Lookup(), audit: audit, launcher: launcher);
        var destination = GitHubExceptionReportTests.Create().NewIssueDestination([]);
        (await workflow.OpenAsync(destination, static () => true, Token)).Should().Be(ExternalBrowserLaunchOutcome.Accepted);
        launcher.Destination.Should().Be(destination.ExactAddress);
        launcher.Calls.Should().Be(1);
        audit.Events.Select(item => item.Outcome).Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded);
        audit.Events.Should().OnlyContain(item => item.ActionId == "network.open-external-browser");
    }

    [Theory]
    [InlineData("https://evil.example/roryprimrose/Kora/issues/new")]
    [InlineData("https://github.com/other/repo/issues/new")]
    [InlineData("https://github.com/roryprimrose/Kora/issues/0")]
    [InlineData("https://github.com/roryprimrose/Kora/issues/no")]
    [InlineData("https://github.com/roryprimrose/Kora/issues/-1")]
    [InlineData("https://github.com/roryprimrose/Kora/issues/1/comments")]
    public async Task Only_canonical_issue_destinations_can_use_the_report_route(string address)
    {
        using var root = Root();
        var launcher = new Launcher();
        await FluentActions.Awaiting(() => Create(new Lookup(), launcher: launcher).OpenAsync(
            ExternalBrowserDestination.Validate(address)!, static () => true, Token)).Should().ThrowAsync<InvalidOperationException>();
        launcher.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Existing_issue_can_open_but_retired_review_cannot()
    {
        using var root = Root();
        var launcher = new Launcher();
        var workflow = Create(new Lookup(), launcher: launcher);
        var destination = ExternalBrowserDestination.Validate("https://github.com/roryprimrose/Kora/issues/12")!;
        await FluentActions.Awaiting(() => workflow.OpenAsync(destination, static () => false, Token))
            .Should().ThrowAsync<InvalidOperationException>();
        (await workflow.OpenAsync(destination, static () => true, Token)).Should().Be(ExternalBrowserLaunchOutcome.Accepted);
        launcher.Calls.Should().Be(1);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static HostActivity Root(RequestOrigin origin = RequestOrigin.LocalUi) =>
        HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Desktop, HostOperation.Evidence);

    private static GitHubIssueReportWorkflow Create(Lookup lookup, Network? network = null, Audit? audit = null, Launcher? launcher = null)
    {
        network ??= new();
        audit ??= new();
        return new(lookup, network, new(launcher ?? new(), network, audit, TimeProvider.System), audit, TimeProvider.System);
    }

    private sealed class Lookup : IGitHubIssueLookup
    {
        public int Calls { get; private set; }
        public string? Fingerprint { get; private set; }
        public Task<GitHubIssueLookupResult> Response { get; set; } = Task.FromResult(new GitHubIssueLookupResult([]));
        public Task<GitHubIssueLookupResult> FindAsync(string fingerprint, Func<bool> admitted, CancellationToken cancellationToken)
        {
            admitted().Should().BeTrue();
            Calls++;
            Fingerprint = fingerprint;
            return Response;
        }
    }

    private sealed class Network : IExternalBrowserNetworkPolicy
    {
        public bool Allowed { get; set; } = true;
        public bool Current { get; set; } = true;
        public ExternalBrowserNetworkAdmission Capture() => new(Allowed, () => Current);
    }

    private sealed class Audit : ISecurityAuditLog
    {
        public List<SecurityAuditEvent> Events { get; } = [];
        public bool Fail { get; set; }
        public void Write(SecurityAuditEvent auditEvent)
        {
            if (Fail) { throw new IOException("audit unavailable"); }
            Events.Add(auditEvent);
        }
    }

    private sealed class Launcher : IExternalBrowserLauncher
    {
        public string? Destination { get; private set; }
        public int Calls { get; private set; }
        public Task<ExternalBrowserLaunchOutcome> LaunchAsync(ExternalBrowserDestination destination, Func<bool> admitted, CancellationToken cancellationToken)
        {
            admitted().Should().BeTrue();
            Calls++;
            Destination = destination.ExactAddress;
            return Task.FromResult(ExternalBrowserLaunchOutcome.Accepted);
        }
    }
}
