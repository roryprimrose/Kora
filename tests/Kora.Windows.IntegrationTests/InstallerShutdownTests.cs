using AwesomeAssertions;

using Kora.Setup;
using Kora.Windows.Coordination;

namespace Kora.Windows.IntegrationTests;

public sealed class InstallerShutdownTests
{
    [Theory]
    [InlineData(SetupAction.Install, InstallScope.CurrentUser)]
    [InlineData(SetupAction.Repair, InstallScope.CurrentUser)]
    [InlineData(SetupAction.Uninstall, InstallScope.CurrentUser)]
    [InlineData(SetupAction.Install, InstallScope.AllUsers)]
    public async Task PlanningWaitsForConfirmedShutdownAndRetainsReservationThroughNativeCompletion(
        SetupAction action, InstallScope scope)
    {
        var engine = new Engine();
        var shutdown = new Shutdown();
        await using var session = new InstallerSession(engine, shutdown: shutdown);
        session.Detected(0, action != SetupAction.Install, installedScope: scope);
        var start = session.StartAsync(action, true, cancellationToken: TestContext.Current.CancellationToken);
        session.Phase.Should().Be(SetupPhase.StoppingHost);
        session.CanClose.Should().BeFalse();
        session.CanCancelShutdown.Should().BeTrue();
        shutdown.Scopes.Should().Equal(scope);
        engine.Actions.Should().BeEmpty();
        var earlyRelease = async () => await session.DisposeAsync();
        await earlyRelease.Should().ThrowAsync<InvalidOperationException>();
        var duplicate = () => session.StartAsync(action, true, cancellationToken: TestContext.Current.CancellationToken);
        await duplicate.Should().ThrowAsync<InvalidOperationException>();
        shutdown.Ready.SetResult(shutdown.Lease);
        await start;
        engine.Actions.Should().Equal(action);
        shutdown.Lease.Released.Should().BeFalse();
        session.Planned(0);
        engine.Applies.Should().Be(1);
        await earlyRelease.Should().ThrowAsync<InvalidOperationException>();
        session.Applied(0, false);
        shutdown.Lease.Released.Should().BeFalse();
        await session.DisposeAsync();
        shutdown.Lease.Released.Should().BeTrue();
        shutdown.Lease.Releases.Should().Be(1);
    }

    [Fact]
    public async Task MissingConsentAndSynchronousBypassCannotRequestShutdown()
    {
        var engine = new Engine();
        var shutdown = new Shutdown();
        await using var session = new InstallerSession(engine, shutdown: shutdown);
        session.Detected(0, false);
        var withoutConsent = () => session.StartAsync(SetupAction.Install, false);
        await withoutConsent.Should().ThrowAsync<InvalidOperationException>();
        var bypass = () => session.Start(SetupAction.Install, true);
        bypass.Should().Throw<InvalidOperationException>();
        shutdown.Scopes.Should().BeEmpty();
        engine.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task LateShutdownSuccessAfterCancellationCannotPlanApplyOrLaunch()
    {
        var engine = new Engine();
        var shutdown = new Shutdown();
        using var cancellation = new CancellationTokenSource();
        await using var session = new InstallerSession(engine, shutdown: shutdown);
        session.Detected(0, false);
        var start = session.StartAsync(SetupAction.Install, true, cancellationToken: cancellation.Token);
        cancellation.Cancel();
        shutdown.Ready.SetResult(shutdown.Lease);
        await start;
        session.Phase.Should().Be(SetupPhase.Cancelled);
        session.ExitCode.Should().Be(InstallerSession.UserCancelled);
        session.CanOfferLaunch.Should().BeFalse();
        engine.Actions.Should().BeEmpty();
        engine.Applies.Should().Be(0);
        await session.DisposeAsync();
        shutdown.Lease.Released.Should().BeTrue();
    }

    [Theory]
    [InlineData("declined")]
    [InlineData("busy")]
    [InlineData("incompatible")]
    [InlineData("unclean owner")]
    public async Task UnconfirmedShutdownNeverAdmitsPackageMutation(string reason)
    {
        var engine = new Engine();
        var shutdown = new Shutdown();
        await using var session = new InstallerSession(engine, shutdown: shutdown);
        session.Detected(0, false);
        var start = session.StartAsync(SetupAction.Install, true, cancellationToken: TestContext.Current.CancellationToken);
        shutdown.Ready.SetException(new InvalidOperationException(reason));
        await start;
        session.Phase.Should().Be(SetupPhase.Failed);
        session.Status.Should().Contain("No package changes").And.Contain(reason);
        session.CanOfferLaunch.Should().BeFalse();
        engine.Actions.Should().BeEmpty();
        engine.Applies.Should().Be(0);
        engine.Failures.Should().ContainSingle();
    }

    [Fact]
    public async Task TimeoutDoesNotBecomeCancellationOrSuccessfulShutdown()
    {
        var engine = new Engine();
        var shutdown = new Shutdown();
        await using var session = new InstallerSession(engine, shutdown: shutdown);
        session.Detected(0, false);
        var start = session.StartAsync(SetupAction.Install, true, cancellationToken: TestContext.Current.CancellationToken);
        shutdown.Ready.SetException(new OperationCanceledException("Shutdown timeout."));
        await start;
        session.Phase.Should().Be(SetupPhase.Failed);
        session.ExitCode.Should().BeNegative();
        engine.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task ChangedUserScopeAfterPlanningCannotStartNativeApply()
    {
        var engine = new Engine();
        var shutdown = new Shutdown { ValidationFailure = new InvalidOperationException("Another user logged on.") };
        await using var session = new InstallerSession(engine, shutdown: shutdown);
        session.Detected(0, false);
        shutdown.Ready.SetResult(shutdown.Lease);
        await session.StartAsync(SetupAction.Install, true, cancellationToken: TestContext.Current.CancellationToken);
        session.Planned(0);
        session.Phase.Should().Be(SetupPhase.Failed);
        session.Status.Should().Contain("No package apply").And.Contain("Another user");
        engine.Applies.Should().Be(0);
        session.CanOfferLaunch.Should().BeFalse();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task CompletionLaunchRequiresSuccessNoRebootAndReleasedMaintenance(bool failed, bool reboot)
    {
        var engine = new Engine();
        var shutdown = new Shutdown();
        var launcher = new Launcher();
        await using var session = new InstallerSession(engine, shutdown: shutdown);
        session.Detected(0, false);
        shutdown.Ready.SetResult(shutdown.Lease);
        await session.StartAsync(SetupAction.Install, true, cancellationToken: TestContext.Current.CancellationToken);
        session.Planned(0);
        session.Applied(failed ? -1 : 0, reboot);
        session.AcceptClose(launchKora: !failed && !reboot);
        var launch = () => session.LaunchAfterCloseAsync(launcher, TestContext.Current.CancellationToken);
        await launch.Should().ThrowAsync<InvalidOperationException>();
        launcher.Launches.Should().Be(0);
        await session.DisposeAsync();
        await launch();
        launcher.Launches.Should().Be(!failed && !reboot ? 1 : 0);
    }

    private sealed class Shutdown : IInstallerShutdown
    {
        public TaskCompletionSource<IInstallerMaintenanceLease> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Reservation Lease { get; } = new();
        public List<InstallScope> Scopes { get; } = [];
        public Exception? ValidationFailure { get; init; }

        public Task<IInstallerMaintenanceLease> AcquireAsync(InstallScope scope, CancellationToken cancellationToken)
        {
            Scopes.Add(scope);
            return Ready.Task;
        }

        public void Validate(InstallScope scope)
        {
            if (ValidationFailure is not null) { throw ValidationFailure; }
        }
    }

    private sealed class Reservation : IInstallerMaintenanceLease
    {
        public bool Released => Releases > 0;
        public int Releases { get; private set; }
        public void BeginNativeApply() { }
        public bool ConfirmNativeApplyComplete() => !Released;
        public ValueTask DisposeAsync() { Releases++; return ValueTask.CompletedTask; }
    }

    private sealed class Launcher : IInstallerApplicationLauncher
    {
        public int Launches { get; private set; }
        public Task LaunchAsync(InstallScope scope, CancellationToken cancellationToken)
        {
            Launches++;
            return Task.CompletedTask;
        }
    }

    private sealed class Engine : IInstallerEngine
    {
        public List<SetupAction> Actions { get; } = [];
        public List<int> Failures { get; } = [];
        public int Applies { get; private set; }
        public void Plan(SetupAction action, InstallScope scope, bool startAtLogin) => Actions.Add(action);
        public void Apply() => Applies++;
        public void LogFailure(int status) => Failures.Add(status);
    }
}
