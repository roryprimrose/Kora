using AwesomeAssertions;
using Kora.Setup;

namespace Kora.Windows.IntegrationTests;

public sealed class InstallerPresentationTests
{
    [Theory]
    [InlineData(InstallScope.CurrentUser)]
    [InlineData(InstallScope.AllUsers)]
    public void UninstallShowsOnlyTheDetectedScopeAndRemovalInformation(InstallScope scope)
    {
        var session = Installed(scope);
        var presentation = new InstallerPresentation(session);
        presentation.Action.Should().Be(SetupAction.Uninstall);
        presentation.Heading.Should().Be("Remove Kora");
        presentation.ShowScopeChoice.Should().BeFalse();
        presentation.ShowScopeDescription.Should().BeTrue();
        presentation.ScopeDescription.Should().Be(scope == InstallScope.CurrentUser
            ? "Current installation: Just for me." : "Current installation: All users.");
        presentation.ShowConfiguration.Should().BeFalse();
        presentation.ShowDependencyRetry.Should().BeFalse();
        presentation.ShowAction.Should().BeTrue();
        presentation.ConsentText.Should().Contain("not your preferences").And.Contain("optional components are kept");
    }

    [Theory]
    [InlineData(InstallScope.CurrentUser)]
    [InlineData(InstallScope.AllUsers)]
    public void RepairKeepsRelevantConfigurationButCannotChangeInstallationScope(InstallScope scope)
    {
        var session = Installed(scope);
        var presentation = new InstallerPresentation(session, SetupAction.Repair);
        presentation.Action.Should().Be(SetupAction.Repair);
        presentation.Heading.Should().Be("Repair Kora");
        presentation.ShowScopeChoice.Should().BeFalse();
        session.CanChooseScope.Should().BeFalse();
        presentation.ShowConfiguration.Should().BeTrue();
        presentation.ShowDependencyRetry.Should().BeTrue();
        presentation.ConsentText.Should().StartWith("Repair").And.Contain("existing scope");
    }

    [Fact]
    public void SwitchingModeOnlyChangesPresentationAndNeverPlansOrApprovesSettings()
    {
        var engine = new Engine();
        var session = Installed(InstallScope.AllUsers, engine);
        var presentation = new InstallerPresentation(session);
        presentation.SwitchAction();
        presentation.Action.Should().Be(SetupAction.Repair);
        presentation.ShowConfiguration.Should().BeTrue();
        presentation.SwitchAction();
        presentation.Action.Should().Be(SetupAction.Uninstall);
        presentation.ShowConfiguration.Should().BeFalse();
        session.Scope.Should().Be(InstallScope.AllUsers);
        session.Phase.Should().Be(SetupPhase.Ready);
        session.StartAtLogin.Should().BeFalse();
        engine.Plans.Should().Be(0);
    }

    [Fact]
    public void InstallKeepsConfigurationAndScopeChoiceWithoutAMaintenanceSwitch()
    {
        var session = new InstallerSession(new Engine());
        session.Detected(0, false);
        var presentation = new InstallerPresentation(session);
        presentation.Action.Should().Be(SetupAction.Install);
        presentation.ShowScopeChoice.Should().BeTrue();
        presentation.ShowConfiguration.Should().BeTrue();
        presentation.CanSwitchAction.Should().BeFalse();
        var change = () => presentation.SwitchAction();
        change.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(SetupAction.Repair)]
    [InlineData(SetupAction.Uninstall)]
    public void ApprovedNativeWorkLocksModeAndKeepsOnlyRelevantSettings(SetupAction action)
    {
        var session = Installed(InstallScope.CurrentUser);
        var presentation = new InstallerPresentation(session, action);
        session.Start(action, consent: true);
        presentation.CanSwitchAction.Should().BeFalse();
        var change = () => presentation.SwitchAction();
        change.Should().Throw<InvalidOperationException>();
        session.Planned(0);
        presentation.CanSwitchAction.Should().BeFalse();
        presentation.ShowScopeChoice.Should().BeFalse();
        presentation.ShowDependencyRetry.Should().BeFalse();
        presentation.ShowConfiguration.Should().Be(action == SetupAction.Repair);
    }

    [Theory]
    [InlineData(SetupAction.Install, "Kora installed")]
    [InlineData(SetupAction.Repair, "Kora repaired")]
    [InlineData(SetupAction.Uninstall, "Kora removed")]
    public void CompletionHidesConfigurationActionsAndApprovalText(SetupAction action, string heading)
    {
        var session = new InstallerSession(new Engine());
        session.Detected(0, action != SetupAction.Install,
            installedScope: action == SetupAction.Install ? null : InstallScope.CurrentUser);
        var presentation = new InstallerPresentation(session,
            action == SetupAction.Install ? SetupAction.Uninstall : action);
        session.Start(action, consent: true);
        session.Planned(0);
        session.Applied(0, restartRequired: false);
        presentation.Heading.Should().Be(heading);
        AssertTerminal(presentation);
        session.CanOfferLaunch.Should().Be(action != SetupAction.Uninstall);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FailedOrCancelledMaintenanceHidesInertConfiguration(bool failed)
    {
        var session = Installed(InstallScope.CurrentUser);
        var presentation = new InstallerPresentation(session, SetupAction.Repair);
        if (failed) { session.Fail(-1); }
        else { session.Cancel(); }
        presentation.Heading.Should().Be(failed ? "Setup incomplete" : "Setup cancelled");
        AssertTerminal(presentation);
    }

    [Fact]
    public void UnknownDetectionNeverPresentsConfigurationOrMaintenanceActions()
    {
        var session = new InstallerSession(new Engine());
        var presentation = new InstallerPresentation(session);
        presentation.Heading.Should().Be("Checking Kora");
        presentation.ShowScopeChoice.Should().BeFalse();
        presentation.ShowConfiguration.Should().BeFalse();
        presentation.ShowAction.Should().BeFalse();
        session.Detected(0, true);
        AssertTerminal(presentation);
    }

    [Theory]
    [InlineData(SetupAction.Repair)]
    [InlineData(SetupAction.Uninstall)]
    public void ExplicitMaintenanceWithNoPackageDoesNotOfferFreshInstallation(SetupAction action)
    {
        var session = new InstallerSession(new Engine());
        session.Detected(0, false);
        var presentation = new InstallerPresentation(session, action, requiresInstalledPackage: true);
        presentation.MissingInstallation.Should().BeTrue();
        presentation.Heading.Should().Be("Kora is not installed");
        presentation.ReadyStatus.Should().Contain("No installed Kora package");
        presentation.ShowAction.Should().BeFalse();
        presentation.ShowConfiguration.Should().BeFalse();
        presentation.ShowScopeChoice.Should().BeFalse();
        presentation.CanSwitchAction.Should().BeFalse();
    }

    private static void AssertTerminal(InstallerPresentation presentation)
    {
        presentation.IsTerminal.Should().BeTrue();
        presentation.ShowConfiguration.Should().BeFalse();
        presentation.ShowScopeChoice.Should().BeFalse();
        presentation.ShowScopeDescription.Should().BeFalse();
        presentation.ShowAction.Should().BeFalse();
        presentation.CanSwitchAction.Should().BeFalse();
        presentation.ShowDependencyRetry.Should().BeFalse();
    }

    private static InstallerSession Installed(InstallScope scope, Engine? engine = null)
    {
        var session = new InstallerSession(engine ?? new Engine());
        session.Detected(0, true, installedScope: scope);
        return session;
    }

    private sealed class Engine : IInstallerEngine
    {
        public int Plans { get; private set; }
        public void Plan(SetupAction action, InstallScope scope, bool startAtLogin) => Plans++;
        public void Apply() { }
        public void LogFailure(int status) { }
    }
}
