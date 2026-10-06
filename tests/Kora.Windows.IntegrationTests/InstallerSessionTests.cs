using AwesomeAssertions;
using Kora.Setup;
using System.Reflection;

namespace Kora.Windows.IntegrationTests;

public sealed class InstallerSessionTests
{
    [Fact]
    public void BootstrapperEntryPointUsesTheApartmentRequiredByBurn()
    {
        var entryPoint = typeof(InstallerSession).Assembly.EntryPoint;
        entryPoint.Should().NotBeNull();
        entryPoint!.GetCustomAttribute<MTAThreadAttribute>().Should().NotBeNull();
        entryPoint.GetCustomAttribute<STAThreadAttribute>().Should().BeNull();
    }

    [Fact]
    public void StartRequiresDetectionAndConsent()
    {
        var engine = new RecordingEngine();
        var session = new InstallerSession(engine);
        var beforeDetection = () => session.Start(SetupAction.Install, true);
        beforeDetection.Should().Throw<InvalidOperationException>();
        session.Detected(0, false);
        var withoutConsent = () => session.Start(SetupAction.Install, false);
        withoutConsent.Should().Throw<InvalidOperationException>();
        engine.Actions.Should().BeEmpty();
    }

    [Fact]
    public void NewInstallationDefaultsToCurrentUser()
    {
        var engine = new RecordingEngine();
        var session = new InstallerSession(engine);
        session.Detected(0, false);
        session.Scope.Should().Be(InstallScope.CurrentUser);
        session.CanChooseScope.Should().BeTrue();
        engine.Actions.Should().BeEmpty();
        session.Start(SetupAction.Install, true);
        engine.Scopes.Should().Equal(InstallScope.CurrentUser);
    }

    [Theory]
    [InlineData(InstallScope.CurrentUser, true)]
    [InlineData(InstallScope.CurrentUser, false)]
    [InlineData(InstallScope.AllUsers, true)]
    [InlineData(InstallScope.AllUsers, false)]
    public void StartupSelectionIsExplicitAndFrozenWithTheNativeScope(InstallScope scope, bool startup)
    {
        var engine = new RecordingEngine();
        var session = new InstallerSession(engine);
        session.Detected(0, false);
        session.SelectScope(scope);
        session.Start(SetupAction.Install, consent: true, startAtLogin: startup);
        session.StartAtLogin.Should().Be(startup);
        engine.Scopes.Should().Equal(scope);
        engine.StartupSelections.Should().Equal(startup);
        var again = () => session.Start(SetupAction.Install, consent: true, startAtLogin: !startup);
        again.Should().Throw<InvalidOperationException>();
        engine.StartupSelections.Should().Equal(startup);
    }

    [Fact]
    public void UninstallCannotEnableStartupBeforePlanning()
    {
        var engine = new RecordingEngine();
        var session = new InstallerSession(engine);
        session.Detected(0, true, installedScope: InstallScope.CurrentUser);
        var start = () => session.Start(SetupAction.Uninstall, consent: true, startAtLogin: true);
        start.Should().Throw<InvalidOperationException>();
        engine.Actions.Should().BeEmpty();
    }

    [Theory]
    [InlineData(InstallScope.CurrentUser)]
    [InlineData(InstallScope.AllUsers)]
    public void ExplicitScopeIsPassedToTheNativePlanAndFrozen(InstallScope scope)
    {
        var engine = new RecordingEngine();
        var session = new InstallerSession(engine);
        session.Detected(0, false);
        session.SelectScope(scope);
        engine.Actions.Should().BeEmpty();
        engine.Scopes.Should().BeEmpty();
        session.Start(SetupAction.Install, true);
        engine.Scopes.Should().Equal(scope);
        session.CanChooseScope.Should().BeFalse();
        var changeScope = () => session.SelectScope(
            scope == InstallScope.CurrentUser ? InstallScope.AllUsers : InstallScope.CurrentUser);
        changeScope.Should().Throw<InvalidOperationException>();
        session.Scope.Should().Be(scope);
    }

    [Theory]
    [InlineData(InstallScope.CurrentUser, SetupAction.Repair)]
    [InlineData(InstallScope.CurrentUser, SetupAction.Uninstall)]
    [InlineData(InstallScope.AllUsers, SetupAction.Repair)]
    [InlineData(InstallScope.AllUsers, SetupAction.Uninstall)]
    public void MaintenanceRetainsDetectedScope(InstallScope scope, SetupAction action)
    {
        var engine = new RecordingEngine();
        var session = new InstallerSession(engine);
        session.Detected(0, true, installedScope: scope);
        session.Scope.Should().Be(scope);
        session.CanChooseScope.Should().BeFalse();
        var changeScope = () => session.SelectScope(
            scope == InstallScope.CurrentUser ? InstallScope.AllUsers : InstallScope.CurrentUser);
        changeScope.Should().Throw<InvalidOperationException>();
        session.Start(action, true);
        engine.Scopes.Should().Equal(scope);
    }

    [Fact]
    public void ExistingBundleOrRelatedPackageScopeIsRetainedWhenCurrentMsiIsAbsent()
    {
        var engine = new RecordingEngine();
        var session = new InstallerSession(engine);
        session.Detected(0, false, installedScope: InstallScope.AllUsers);
        session.CanChooseScope.Should().BeFalse();
        session.Start(SetupAction.Install, true);
        engine.Scopes.Should().Equal(InstallScope.AllUsers);
    }

    [Theory]
    [InlineData(null)]
    [InlineData((InstallScope)99)]
    public void UnknownInstalledScopeFailsBeforePlanning(InstallScope? scope)
    {
        var engine = new RecordingEngine();
        var session = new InstallerSession(engine);
        session.Detected(0, true, installedScope: scope);
        session.Phase.Should().Be(SetupPhase.Failed);
        session.Status.Should().Contain("scope is unknown");
        session.CanStart.Should().BeFalse();
        engine.Actions.Should().BeEmpty();
        engine.Failures.Should().ContainSingle();
    }

    [Fact]
    public void ScopeRequiresDetectionAndRejectsUndefinedValues()
    {
        var session = new InstallerSession(new RecordingEngine());
        var beforeDetection = () => session.SelectScope(InstallScope.AllUsers);
        beforeDetection.Should().Throw<InvalidOperationException>();
        session.Detected(0, false);
        var invalid = () => session.SelectScope((InstallScope)99);
        invalid.Should().Throw<InvalidOperationException>();
        session.Scope.Should().Be(InstallScope.CurrentUser);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, null)]
    public void DetectionFailsClosed(int status, bool? installed)
    {
        var engine = new RecordingEngine();
        var session = new InstallerSession(engine);
        session.Detected(status, installed);
        session.Phase.Should().Be(SetupPhase.Failed);
        session.CanStart.Should().BeFalse();
        session.ExitCode.Should().BeNegative();
        engine.Failures.Should().ContainSingle();
    }

    [Fact]
    public void UnsupportedPlatformCannotPlanOrInstallPrerequisites()
    {
        var engine = new RecordingEngine();
        var session = new InstallerSession(engine);
        session.Detected(0, false, supportedPlatform: false);
        session.Phase.Should().Be(SetupPhase.Failed);
        session.Status.Should().Contain("Windows 11 x64").And.Contain("No prerequisites");
        session.CanStart.Should().BeFalse();
        var start = () => session.Start(SetupAction.Install, true);
        start.Should().Throw<InvalidOperationException>();
        engine.Actions.Should().BeEmpty();
        engine.ApplyCount.Should().Be(0);
        engine.Failures.Should().ContainSingle();
    }

    [Theory]
    [InlineData(SetupAction.Repair)]
    [InlineData(SetupAction.Uninstall)]
    [InlineData((SetupAction)99)]
    public void AbsentPackageRejectsInvalidActions(SetupAction action)
    {
        var session = new InstallerSession(new RecordingEngine());
        session.Detected(0, false);
        var start = () => session.Start(action, true);
        start.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(SetupAction.Install)]
    [InlineData(SetupAction.Repair)]
    [InlineData(SetupAction.Uninstall)]
    public void LifecyclePlansThenAppliesAndReportsSuccess(SetupAction action)
    {
        var engine = new RecordingEngine();
        var session = new InstallerSession(engine);
        session.Detected(0, action != SetupAction.Install,
            installedScope: action != SetupAction.Install ? InstallScope.CurrentUser : null);
        session.Start(action, true);
        session.Phase.Should().Be(SetupPhase.Planning);
        session.CanClose.Should().BeFalse();
        engine.Actions.Should().Equal(action);
        engine.ApplyCount.Should().Be(0);
        session.Planned(0);
        session.Phase.Should().Be(SetupPhase.Applying);
        engine.ApplyCount.Should().Be(1);
        session.ReportProgress(42);
        session.Progress.Should().Be(42);
        session.Applied(0, false);
        session.Phase.Should().Be(SetupPhase.Succeeded);
        session.ExitCode.Should().Be(0);
        session.Progress.Should().Be(100);
        session.CanClose.Should().BeTrue();
    }

    [Fact]
    public void FailedPlanDoesNotApply()
    {
        var engine = new RecordingEngine();
        var session = new InstallerSession(engine);
        session.Detected(0, false);
        session.Start(SetupAction.Install, true);
        session.Planned(-1);
        session.ExitCode.Should().Be(-1);
        session.Phase.Should().Be(SetupPhase.Failed);
        engine.ApplyCount.Should().Be(0);
        engine.Failures.Should().Equal(-1);
    }

    [Fact]
    public void ApplyFailureIsNotReportedAsSuccess()
    {
        var session = new InstallerSession(new RecordingEngine());
        session.Detected(0, false);
        session.Start(SetupAction.Install, true);
        session.Planned(0);
        session.Applied(-1, false);
        session.Phase.Should().Be(SetupPhase.Failed);
        session.ExitCode.Should().Be(-1);
        session.Status.Should().Contain("0xFFFFFFFF");
    }

    [Fact]
    public void RestartRequiredIsReturnedWithoutRestartingWindows()
    {
        var session = new InstallerSession(new RecordingEngine());
        session.Detected(0, false);
        session.Start(SetupAction.Install, true);
        session.Planned(0);
        session.Applied(0, true);
        session.ExitCode.Should().Be(InstallerSession.RebootRequired);
        session.Status.Should().Contain("will not restart");
    }

    [Fact]
    public void CloseBeforeApplyCancelsWithoutPackageChanges()
    {
        var engine = new RecordingEngine();
        var session = new InstallerSession(engine);
        session.Cancel();
        session.ExitCode.Should().Be(InstallerSession.UserCancelled);
        session.Phase.Should().Be(SetupPhase.Cancelled);
        engine.Actions.Should().BeEmpty();
    }

    [Fact]
    public void CloseDuringApplyIsBlockedAndDoesNotFabricateCancellation()
    {
        var session = new InstallerSession(new RecordingEngine());
        session.Detected(0, false);
        session.Start(SetupAction.Install, true);
        session.Planned(0);
        var cancel = () => session.Cancel();
        cancel.Should().Throw<InvalidOperationException>();
        session.Phase.Should().Be(SetupPhase.Applying);
    }

    [Fact]
    public void DuplicatePlansAreRejected()
    {
        var engine = new RecordingEngine();
        var session = new InstallerSession(engine);
        session.Detected(0, false);
        session.Start(SetupAction.Install, true);
        var duplicate = () => session.Start(SetupAction.Install, true);
        duplicate.Should().Throw<InvalidOperationException>();
        engine.Actions.Should().ContainSingle();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void ProgressOutsidePercentageRangeIsRejected(int percentage)
    {
        var session = new InstallerSession(new RecordingEngine());
        session.Detected(0, false);
        session.Start(SetupAction.Install, true);
        session.Planned(0);
        var progress = () => session.ReportProgress(percentage);
        progress.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void UnexpectedLifecycleCallbacksDoNotChangePackageState()
    {
        var engine = new RecordingEngine();
        var session = new InstallerSession(engine);
        var plan = () => session.Planned(0);
        var apply = () => session.Applied(0, false);
        var progress = () => session.ReportProgress(42);
        plan.Should().Throw<InvalidOperationException>();
        apply.Should().Throw<InvalidOperationException>();
        progress.Should().Throw<InvalidOperationException>();
        engine.ApplyCount.Should().Be(0);
        engine.Actions.Should().BeEmpty();
    }

    [Fact]
    public void FailureCannotReturnSuccessfulExitCode()
    {
        var session = new InstallerSession(new RecordingEngine());
        var fail = () => session.Fail(0);
        fail.Should().Throw<ArgumentOutOfRangeException>();
    }

    private sealed class RecordingEngine : IInstallerEngine
    {
        public List<SetupAction> Actions { get; } = [];

        public List<int> Failures { get; } = [];

        public List<InstallScope> Scopes { get; } = [];

        public int ApplyCount { get; private set; }

        public List<bool> StartupSelections { get; } = [];

        public void Plan(SetupAction action, InstallScope scope, bool startAtLogin)
        {
            Actions.Add(action);
            Scopes.Add(scope);
            StartupSelections.Add(startAtLogin);
        }

        public void Apply() => ApplyCount++;

        public void LogFailure(int status) => Failures.Add(status);
    }
}
