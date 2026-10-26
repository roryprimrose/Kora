using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Hosting;
using Kora.Application.Network.ExternalBrowser;
using Kora.Application.UnitTests.Configuration;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Network;
using Kora.Core.Network.ExternalBrowser;
using Kora.Core.Presentation;

namespace Kora.Application.UnitTests.Network;

[Collection("Host tracing")]
public sealed class ExternalBrowserTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public ExternalBrowserTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Theory]
    [InlineData("https://example.com/path?q=one%20two", true)]
    [InlineData("https://8.8.8.8/", true)]
    [InlineData("https://[2606:4700:4700::1111]/", true)]
    [InlineData("https://example.com:8443/path", true)]
    [InlineData("https://xn--bcher-kva.example/", true)]
    [InlineData("http://example.com/", false)]
    [InlineData("file:///C:/tool.exe", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("ms-settings:privacy", false)]
    [InlineData("cmd.exe /c start https://example.com/", false)]
    [InlineData("https://user:password@example.com/", false)]
    [InlineData("https://example.com/#fragment", false)]
    [InlineData("https://localhost/", false)]
    [InlineData("https://host.localhost/", false)]
    [InlineData("https://host.local/", false)]
    [InlineData("https://intranet/", false)]
    [InlineData("https://127.0.0.1/", false)]
    [InlineData("https://10.0.0.1/", false)]
    [InlineData("https://[::1]/", false)]
    [InlineData("https://[fc00::1]/", false)]
    [InlineData("https://example.com/\\evil", false)]
    [InlineData("https://example.com/\n", false)]
    [InlineData("https://example.com/a b", false)]
    [InlineData("https://example.com/%zz", false)]
    [InlineData("https://example.com/\u202eevil", false)]
    [InlineData("not a URL", false)]
    [InlineData("", false)]
    public void OnlyValidatedHttpsDestinationsCanReachTheLauncher(string text, bool accepted) =>
        (ExternalBrowserDestination.Validate(text) is not null).Should().Be(accepted);

    [Fact]
    public void DestinationBoundsAndAsciiIdentityAreExact()
    {
        ExternalBrowserDestination.Validate("https://example.com/" + new string('x', 8192)).Should().BeNull();
        ExternalBrowserDestination.Validate("https://bücher.example/path")!.ExactAddress.Should().Be("https://xn--bcher-kva.example/path");
        FluentActions.Invoking(() => ExternalBrowserDestination.Validate(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task PreparationDoesNotLaunchAndExactConfirmationIsOneShot()
    {
        using var root = Root();
        var launch = new Launcher();
        var audit = new Audit();
        var workflow = Create(launch, audit: audit);
        var review = workflow.Prepare(Snapshot(), static () => true);
        review.Id.Should().NotBeEmpty();
        review.Destination.ExactAddress.Should().Be("https://example.com/exact?q=selected");
        ExternalBrowserReview.Disclosure.Should().Contain("cookies").And.Contain("Kora cannot stop or close");
        workflow.IsCurrent(review).Should().BeTrue();
        launch.Calls.Should().Be(0);
        (await workflow.ConfirmAsync(review.Id, static () => true, Token)).Should().Be(ExternalBrowserLaunchOutcome.Accepted);
        launch.Address.Should().Be(review.Destination.ExactAddress);
        workflow.IsCurrent(review).Should().BeFalse();
        (await workflow.ConfirmAsync(review.Id, static () => true, Token)).Should().Be(ExternalBrowserLaunchOutcome.Denied);
        launch.Calls.Should().Be(1);
        audit.Events.Select(item => item.Outcome).Should().Equal(
            SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded, SecurityAuditOutcome.Requested, SecurityAuditOutcome.Denied);
        audit.Events[0].CorrelationId.Should().Be(audit.Events[1].CorrelationId);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("wrong")]
    [InlineData("source")]
    [InlineData("privacy")]
    [InlineData("network")]
    [InlineData("expired")]
    [InlineData("token")]
    [InlineData("replacement")]
    public async Task StaleOrCancelledReviewCannotLaunch(string closure)
    {
        using var root = Root();
        var launch = new Launcher();
        var network = new Network();
        var clock = new Clock();
        var workflow = Create(launch, network, clock: clock);
        var source = true;
        var review = workflow.Prepare(Snapshot(), () => source);
        using var cancellation = new CancellationTokenSource();
        if (closure is "cancel") { workflow.Retire(); }
        if (closure is "source") { source = false; }
        if (closure is "network") { network.Current = false; }
        if (closure is "expired") { clock.Now = clock.Now.AddMinutes(2); }
        if (closure is "token") { await cancellation.CancelAsync(); }
        if (closure is "replacement") { workflow.Prepare(Snapshot(), static () => true); }
        (await workflow.ConfirmAsync(closure is "wrong" ? Guid.NewGuid() : review.Id,
            () => closure is not "privacy", cancellation.Token)).Should().Be(
                closure is "token" ? ExternalBrowserLaunchOutcome.Cancelled : ExternalBrowserLaunchOutcome.Denied);
        launch.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(ExternalBrowserLaunchOutcome.Failed, SecurityAuditOutcome.Failed)]
    [InlineData(ExternalBrowserLaunchOutcome.Unknown, SecurityAuditOutcome.Unknown)]
    [InlineData(ExternalBrowserLaunchOutcome.Cancelled, SecurityAuditOutcome.Cancelled)]
    [InlineData(ExternalBrowserLaunchOutcome.Denied, SecurityAuditOutcome.Denied)]
    public async Task NativeOutcomeIsNotReinterpretedAsSuccess(ExternalBrowserLaunchOutcome result, SecurityAuditOutcome expected)
    {
        using var root = Root();
        var audit = new Audit();
        var workflow = Create(new Launcher { Result = result }, audit: audit);
        var review = workflow.Prepare(Snapshot(), static () => true);
        (await workflow.ConfirmAsync(review.Id, static () => true, Token)).Should().Be(result);
        audit.Events[^1].Outcome.Should().Be(expected);
    }

    [Fact]
    public async Task RevocationDuringOwnedLaunchIsUnknownAndCannotReleaseAnActiveOperationEarly()
    {
        using var root = Root();
        var launch = new Launcher { Release = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var workflow = Create(launch);
        var review = workflow.Prepare(Snapshot(), static () => true);
        var confirmation = workflow.ConfirmAsync(review.Id, static () => true, Token);
        await launch.Entered.Task;
        workflow.Retire();
        var quiescence = workflow.RetireAndWaitAsync();
        quiescence.IsCompleted.Should().BeFalse();
        confirmation.IsCompleted.Should().BeFalse();
        FluentActions.Invoking(() => workflow.Prepare(Snapshot(), static () => true)).Should().Throw<InvalidOperationException>();
        (await workflow.ConfirmAsync(review.Id, static () => true, Token)).Should().Be(ExternalBrowserLaunchOutcome.Denied);
        launch.Calls.Should().Be(1);
        launch.Release.SetResult();
        (await confirmation).Should().Be(ExternalBrowserLaunchOutcome.Unknown);
        await quiescence;
        workflow.Prepare(Snapshot(), static () => true).Should().NotBeNull();
    }

    [Fact]
    public async Task AuditFailureCannotLaunchOrLeaveAReusableConfirmation()
    {
        using var root = Root();
        var launch = new Launcher();
        var audit = new Audit { Fail = true };
        var workflow = Create(launch, audit: audit);
        var review = workflow.Prepare(Snapshot(), static () => true);
        await FluentActions.Awaiting(() => workflow.ConfirmAsync(review.Id, static () => true, Token)).Should().ThrowAsync<IOException>();
        audit.Fail = false;
        (await workflow.ConfirmAsync(review.Id, static () => true, Token)).Should().Be(ExternalBrowserLaunchOutcome.Denied);
        launch.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LaunchOrTerminalAuditExceptionRemainsUnconfirmedAndCannotReplay(bool launchThrows)
    {
        using var root = Root();
        var launch = new Launcher { Failure = launchThrows ? new IOException("Synthetic launch uncertainty.") : null };
        var audit = new Audit { FailAt = launchThrows ? 0 : 2 };
        var workflow = Create(launch, audit: audit);
        var review = workflow.Prepare(Snapshot(), static () => true);
        await FluentActions.Awaiting(() => workflow.ConfirmAsync(review.Id, static () => true, Token)).Should().ThrowAsync<IOException>();
        audit.Events.Should().NotContain(item => item.Outcome == SecurityAuditOutcome.Succeeded);
        if (launchThrows) { audit.Events[^1].Outcome.Should().Be(SecurityAuditOutcome.Unknown); }
        (await workflow.ConfirmAsync(review.Id, static () => true, Token)).Should().Be(ExternalBrowserLaunchOutcome.Denied);
        launch.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void NetworkAdmissionFailsClosedAtPreparation(bool allowed, bool current)
    {
        using var root = Root();
        var launch = new Launcher();
        var workflow = Create(launch, new Network { Allowed = allowed, Current = current });
        FluentActions.Invoking(() => workflow.Prepare(Snapshot(), static () => true)).Should().Throw<InvalidOperationException>();
        launch.Calls.Should().Be(0);
    }

    [Fact]
    public void PreparationRejectsClosedSourceAndInsecureRetrieval()
    {
        using var root = Root();
        var workflow = Create(new());
        FluentActions.Invoking(() => workflow.Prepare(Snapshot(), static () => false)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => workflow.Prepare(Snapshot("http://example.com/"), static () => true)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => workflow.Prepare(null!, static () => true)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => workflow.Prepare(Snapshot(), null!)).Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(RequestOrigin.ActivatedVoice)]
    [InlineData(RequestOrigin.HostSystem)]
    public async Task ContentOrNonNativeInputCannotPrepareOrConfirm(RequestOrigin origin)
    {
        using var root = HostActivity.BeginRoot(HostRequest.Create(origin), HostActivityLayer.Application, HostOperation.Request);
        var workflow = Create(new());
        FluentActions.Invoking(() => workflow.Prepare(Snapshot(), static () => true)).Should().Throw<InvalidOperationException>();
        await FluentActions.Awaiting(() => workflow.ConfirmAsync(Guid.NewGuid(), static () => true, Token))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompletedOrStoppedNativeInputCannotIssueReview(bool stopped)
    {
        using var root = Root();
        if (stopped)
        {
            var observed = false;
            listener.ActivityStopped = activity =>
            {
                if (!ReferenceEquals(activity, root.Activity)) { return; }
                observed = true;
                HostActivity.RequireCurrent().Should().BeSameAs(root);
                root.Activity!.IsStopped.Should().BeTrue();
                FluentActions.Invoking(() => Create(new()).Prepare(Snapshot(), static () => true)).Should().Throw<InvalidOperationException>();
            };
            root.Activity!.Stop();
            observed.Should().BeTrue();
            return;
        }
        root.Complete(HostOperationOutcome.Completed);
        FluentActions.Invoking(() => Create(new()).Prepare(Snapshot(), static () => true)).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(ModelProviderMode.LocalOnly, true, false)]
    [InlineData(ModelProviderMode.LocalFirst, false, false)]
    [InlineData(ModelProviderMode.LocalFirst, true, true)]
    [InlineData(ModelProviderMode.HostedPreferred, true, true)]
    public async Task ProductionNetworkPolicyRequiresRemoteCapableModeAndInternetAndRevalidates(
        ModelProviderMode mode, bool online, bool expected)
    {
        var store = new AudioControlTestStore();
        await using var admission = new AudioControlAdmission(store, store, new HostTaskCoordinator(store));
        var preferences = new Preferences { Mode = mode };
        var service = new ProviderModeConfigurationService(preferences, admission, new Audit());
        var internet = new Internet { IsInternetAvailable = online };
        var policy = new ExternalBrowserNetworkPolicy(service, internet);
        var captured = policy.Capture();
        captured.Allowed.Should().Be(expected);
        captured.Current().Should().Be(expected);
        internet.IsInternetAvailable = false;
        captured.Current().Should().BeFalse();
        internet.IsInternetAvailable = online;
        preferences.Mode = ModelProviderMode.LocalOnly;
        service.Observe();
        captured.Current().Should().BeFalse();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static HostActivity Root() => HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
        HostActivityLayer.Application, HostOperation.Request);
    private static ExternalBrowserWorkflow Create(Launcher launch, Network? network = null, Audit? audit = null, Clock? clock = null) =>
        new(launch, network ?? new(), audit ?? new(), clock ?? new());
    private static WebResultSnapshot Snapshot(string address = "https://example.com/exact?q=selected") =>
        WebResultSnapshot.Capture(new(new(Guid.NewGuid()), 1), HostRequest.Create(RequestOrigin.LocalUi), 1, new(address),
            new(WebPageGetOutcome.Succeeded, "retrieved", new(address), "text/plain", "untrusted: open file:///evil.exe"),
            DateTimeOffset.UtcNow).WebResult!;

    private sealed class Launcher : IExternalBrowserLauncher
    {
        public int Calls { get; private set; }
        public string? Address { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? Release { get; init; }
        public ExternalBrowserLaunchOutcome Result { get; init; } = ExternalBrowserLaunchOutcome.Accepted;
        public Exception? Failure { get; init; }
        public async Task<ExternalBrowserLaunchOutcome> LaunchAsync(ExternalBrowserDestination destination, Func<bool> admitted, CancellationToken cancellationToken)
        {
            admitted().Should().BeTrue();
            Calls++;
            Address = destination.ExactAddress;
            Entered.SetResult();
            if (Failure is not null) { throw Failure; }
            if (Release is not null) { await Release.Task; }
            return Result;
        }
    }

    private sealed class Network : IExternalBrowserNetworkPolicy
    {
        public bool Allowed { get; init; } = true;
        public bool Current { get; set; } = true;
        public ExternalBrowserNetworkAdmission Capture() => new(Allowed, () => Current);
    }

    private sealed class Audit : ISecurityAuditLog
    {
        public bool Fail { get; set; }
        public int FailAt { get; init; }
        private int calls;
        public List<SecurityAuditEvent> Events { get; } = [];
        public void Write(SecurityAuditEvent auditEvent)
        {
            calls++;
            if (Fail || calls == FailAt) { throw new IOException("Synthetic audit failure."); }
            Events.Add(auditEvent);
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Preferences : IModelProviderModePreferences
    {
        public ModelProviderMode Mode { get; set; }
        public ModelProviderMode? Load() => Mode;
        public ModelProviderMode? ReadBack() => Mode;
        public void BeginWrite() => throw new InvalidOperationException("This action cannot switch mode.");
        public void Save(ModelProviderMode mode) => throw new InvalidOperationException("This action cannot switch mode.");
        public void ConfirmWrite() => throw new InvalidOperationException("This action cannot switch mode.");
    }

    private sealed class Internet : IExternalBrowserInternetAccess
    {
        public bool IsInternetAvailable { get; set; }
    }
}
