using AwesomeAssertions;
using Kora.Setup;

namespace Kora.Windows.IntegrationTests;

public sealed class InstallerCompletionLaunchTests
{
    [Theory]
    [InlineData(SetupAction.Install, InstallScope.CurrentUser)]
    [InlineData(SetupAction.Install, InstallScope.AllUsers)]
    [InlineData(SetupAction.Repair, InstallScope.CurrentUser)]
    [InlineData(SetupAction.Repair, InstallScope.AllUsers)]
    public async Task SuccessfulInstallOrRepairLaunchesOnlyAfterOneApprovedClose(SetupAction action, InstallScope scope)
    {
        var session = Completed(action, scope);
        var launcher = new Launcher();
        session.CanLaunchOnClose.Should().BeTrue();
        var beforeClose = () => session.LaunchAfterCloseAsync(launcher, TestContext.Current.CancellationToken);
        await beforeClose.Should().ThrowAsync<InvalidOperationException>();
        launcher.Scopes.Should().BeEmpty();
        session.AcceptClose(launchKora: true);
        await session.LaunchAfterCloseAsync(launcher, TestContext.Current.CancellationToken);
        launcher.Scopes.Should().Equal(scope);
        var duplicate = () => session.LaunchAfterCloseAsync(launcher, TestContext.Current.CancellationToken);
        await duplicate.Should().ThrowAsync<InvalidOperationException>();
        launcher.Scopes.Should().ContainSingle();
    }

    [Fact]
    public async Task OptOutClosesWithoutLaunching()
    {
        var session = Completed(SetupAction.Install, InstallScope.CurrentUser);
        var launcher = new Launcher();
        session.AcceptClose(launchKora: false);
        await session.LaunchAfterCloseAsync(launcher, TestContext.Current.CancellationToken);
        launcher.Scopes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(SetupAction.Uninstall, false)]
    [InlineData(SetupAction.Install, true)]
    [InlineData(SetupAction.Repair, true)]
    public void UninstallAndRequiredRestartCannotApproveLaunch(SetupAction action, bool restart)
    {
        var session = Completed(action, InstallScope.CurrentUser, restart);
        session.CanLaunchOnClose.Should().BeFalse();
        var close = () => session.AcceptClose(launchKora: true);
        close.Should().Throw<InvalidOperationException>();
        session.AcceptClose(launchKora: false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailureOrCancellationCannotApproveLaunch(bool fail)
    {
        var session = new InstallerSession(new Engine());
        session.Detected(0, false);
        if (fail) { session.Fail(-1); }
        else { session.Cancel(); }
        session.CanOfferLaunch.Should().BeFalse();
        var close = () => session.AcceptClose(launchKora: true);
        close.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ActiveNativeWorkCannotCloseOrApproveLaunch()
    {
        var session = new InstallerSession(new Engine());
        session.Detected(0, false);
        session.Start(SetupAction.Install, consent: true);
        var planningClose = () => session.AcceptClose(launchKora: true);
        planningClose.Should().Throw<InvalidOperationException>();
        session.Planned(0);
        var applyingClose = () => session.AcceptClose(launchKora: false);
        applyingClose.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task LaunchFailureIsNotSilentlyRetriedOrReportedAsSuccess()
    {
        var session = Completed(SetupAction.Install, InstallScope.CurrentUser);
        var launcher = new Launcher { Failure = new IOException("Missing application.") };
        session.AcceptClose(launchKora: true);
        var launch = () => session.LaunchAfterCloseAsync(launcher, TestContext.Current.CancellationToken);
        await launch.Should().ThrowAsync<IOException>();
        await launch.Should().ThrowAsync<InvalidOperationException>();
        launcher.Scopes.Should().ContainSingle();
    }

    private static InstallerSession Completed(SetupAction action, InstallScope scope, bool restart = false)
    {
        var session = new InstallerSession(new Engine());
        session.Detected(0, action != SetupAction.Install, installedScope: scope);
        session.Start(action, consent: true);
        session.Planned(0);
        session.Applied(0, restart);
        return session;
    }

    private sealed class Launcher : IInstallerApplicationLauncher
    {
        public List<InstallScope> Scopes { get; } = [];
        public Exception? Failure { get; init; }

        public Task LaunchAsync(InstallScope scope, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Scopes.Add(scope);
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }

    private sealed class Engine : IInstallerEngine
    {
        public void Plan(SetupAction action, InstallScope scope, bool startAtLogin) { }
        public void Apply() { }
        public void LogFailure(int status) { }
    }
}
