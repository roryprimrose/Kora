using AwesomeAssertions;
using Kora.Setup;

namespace Kora.Windows.IntegrationTests;

public sealed class InstallerOptionalSetupTests
{
    [Fact]
    public void OptionsAreUncheckedByDefault()
    {
        new OptionalComponents().Any.Should().BeFalse();
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public async Task OnlyTheExplicitSelectionIsPassedAfterNativeApply(bool powerShell, bool inference, bool kokoro)
    {
        var optional = new RecordingSetup();
        var session = new InstallerSession(new RecordingEngine(), optional);
        var selection = new OptionalComponents(powerShell, inference, kokoro);
        session.Detected(0, false);
        session.Start(SetupAction.Install, true, selection);
        optional.Calls.Should().Be(0);
        session.Planned(0);
        optional.Calls.Should().Be(0);
        session.Applied(0, false);
        session.Phase.Should().Be(SetupPhase.PreparingOptional);
        session.CanClose.Should().BeFalse();
        session.CanCancelOptional.Should().BeTrue();
        session.CanLaunchOnClose.Should().BeFalse();
        await session.PrepareOptionalAsync(TestContext.Current.CancellationToken);
        optional.Selection.Should().Be(selection);
        session.Phase.Should().Be(SetupPhase.Succeeded);
        session.ExitCode.Should().Be(0);
        session.CanLaunchOnClose.Should().BeTrue();
    }

    [Fact]
    public void KoraOnlyAndUninstallDoNotPrepareOptionalComponents()
    {
        var optional = new RecordingSetup();
        var session = new InstallerSession(new RecordingEngine(), optional);
        session.Detected(0, true, installedScope: InstallScope.CurrentUser);
        session.Start(SetupAction.Uninstall, true);
        session.Planned(0);
        session.Applied(0, false);
        optional.Calls.Should().Be(0);
        session.Phase.Should().Be(SetupPhase.Succeeded);
    }

    [Fact]
    public void SelectionOnUninstallIsRejectedBeforePlanning()
    {
        var engine = new RecordingEngine();
        var session = new InstallerSession(engine, new RecordingSetup());
        session.Detected(0, true, installedScope: InstallScope.CurrentUser);
        var start = () => session.Start(SetupAction.Uninstall, true, new OptionalComponents(Kokoro: true));
        start.Should().Throw<InvalidOperationException>();
        engine.Plans.Should().Be(0);
    }

    [Fact]
    public void SelectionWithoutSetupServiceIsRejectedBeforePlanning()
    {
        var engine = new RecordingEngine();
        var session = new InstallerSession(engine);
        session.Detected(0, false);
        var start = () => session.Start(SetupAction.Install, true, new OptionalComponents(PowerShell: true));
        start.Should().Throw<InvalidOperationException>();
        engine.Plans.Should().Be(0);
    }

    [Fact]
    public async Task OptionalFailureIsExplicitAndDoesNotClaimRollbackOfInstalledKora()
    {
        var engine = new RecordingEngine();
        var optional = new RecordingSetup { Failure = new IOException("Download verification failed.") };
        var session = CreatePending(engine, optional);
        await session.PrepareOptionalAsync(TestContext.Current.CancellationToken);
        session.Phase.Should().Be(SetupPhase.Failed);
        session.Status.Should().Contain("Kora remains installed").And.Contain("Download verification failed");
        session.ExitCode.Should().BeNegative();
        engine.Failures.Should().ContainSingle();
        session.CanClose.Should().BeTrue();
        session.CanLaunchOnClose.Should().BeFalse();
    }

    [Fact]
    public async Task AssetIdentityFailureIsExplicitAfterCoreInstallation()
    {
        var engine = new RecordingEngine();
        var session = CreatePending(engine, new RecordingSetup
        {
            Failure = new InvalidDataException("Asset digest does not match."),
        });
        await session.PrepareOptionalAsync(TestContext.Current.CancellationToken);
        session.Phase.Should().Be(SetupPhase.Failed);
        session.Status.Should().Contain("Kora remains installed").And.Contain("digest");
        session.CanLaunchOnClose.Should().BeFalse();
        engine.Failures.Should().ContainSingle();
    }

    [Fact]
    public async Task OptionalCancellationRetainsInstalledKoraAndReportsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var session = CreatePending(new RecordingEngine(), new RecordingSetup());
        await session.PrepareOptionalAsync(cancellation.Token);
        session.Phase.Should().Be(SetupPhase.Cancelled);
        session.ExitCode.Should().Be(InstallerSession.UserCancelled);
        session.Status.Should().Contain("Kora remains installed").And.Contain("cancelled");
        session.CanLaunchOnClose.Should().BeFalse();
        session.CanClose.Should().BeTrue();
    }

    [Fact]
    public async Task NativeRestartRequirementSurvivesOptionalCompletion()
    {
        var session = CreatePending(new RecordingEngine(), new RecordingSetup(), restart: true);
        await session.PrepareOptionalAsync(TestContext.Current.CancellationToken);
        session.ExitCode.Should().Be(InstallerSession.RebootRequired);
        session.Status.Should().Contain("will not restart");
        session.CanLaunchOnClose.Should().BeFalse();
    }

    [Fact]
    public void NativeApplyFailureNeverStartsOptionalPreparation()
    {
        var optional = new RecordingSetup();
        var session = new InstallerSession(new RecordingEngine(), optional);
        session.Detected(0, false);
        session.Start(SetupAction.Install, true, new OptionalComponents(Kokoro: true));
        session.Planned(0);
        session.Applied(-1, false);
        session.Phase.Should().Be(SetupPhase.Failed);
        optional.Calls.Should().Be(0);
    }

    [Fact]
    public async Task DuplicateOptionalPreparationIsRejected()
    {
        var optional = new RecordingSetup();
        var session = CreatePending(new RecordingEngine(), optional);
        await session.PrepareOptionalAsync(TestContext.Current.CancellationToken);
        var duplicate = () => session.PrepareOptionalAsync(TestContext.Current.CancellationToken);
        await duplicate.Should().ThrowAsync<InvalidOperationException>();
        optional.Calls.Should().Be(1);
    }

    [Fact]
    public async Task DependencyTimeoutIsReportedAsFailureNotUserCancellation()
    {
        var engine = new RecordingEngine();
        var session = new InstallerSession(engine, new TimingOutSetup());
        session.Detected(0, false);
        session.Start(SetupAction.Install, true, new OptionalComponents(Kokoro: true));
        session.Planned(0);
        session.Applied(0, false);
        await session.PrepareOptionalAsync(TestContext.Current.CancellationToken);
        session.Phase.Should().Be(SetupPhase.Failed);
        session.Status.Should().Contain("timed out").And.Contain("Kora remains installed");
        engine.Failures.Should().ContainSingle();
    }

    private static InstallerSession CreatePending(RecordingEngine engine, RecordingSetup optional, bool restart = false)
    {
        var session = new InstallerSession(engine, optional);
        session.Detected(0, false);
        session.Start(SetupAction.Install, true, new OptionalComponents(Kokoro: true));
        session.Planned(0);
        session.Applied(0, restart);
        return session;
    }

    private sealed class RecordingSetup : IOptionalComponentSetup
    {
        public int Calls { get; private set; }

        public OptionalComponents? Selection { get; private set; }

        public Exception? Failure { get; init; }

        public Task PrepareAsync(OptionalComponents components, IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            Calls++;
            Selection = components;
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingEngine : IInstallerEngine
    {
        public int Plans { get; private set; }

        public List<int> Failures { get; } = [];

        public void Plan(SetupAction action, InstallScope scope, bool startAtLogin) => Plans++;

        public void Apply() { }

        public void LogFailure(int status) => Failures.Add(status);
    }

    private sealed class TimingOutSetup : IOptionalComponentSetup
    {
        public Task PrepareAsync(OptionalComponents components, IProgress<string> progress,
            CancellationToken cancellationToken) => throw new OperationCanceledException("Dependency timed out.");
    }
}
