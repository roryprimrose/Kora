using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Windows.Dependencies;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed class WindowsPowerShellSetupServiceTests
{
    [Fact]
    public void Winget_installation_uses_the_official_user_package_without_scripting()
    {
        var command = PowerShellProcessRunner.CreateInstallationStartInfo();

        command.FileName.Should().EndWith(Path.Combine("Microsoft", "WindowsApps", "winget.exe"));
        command.UseShellExecute.Should().BeFalse();
        command.ArgumentList.Should().ContainInOrder(
            "install", "--id", "Microsoft.PowerShell", "--exact",
            "--scope", "user", "--source", "winget",
            "--accept-package-agreements", "--accept-source-agreements",
            "--disable-interactivity");
    }

    [Fact]
    public async Task Missing_runtime_is_a_queued_setup_need()
    {
        using var fixture = new Fixture();
        using var bootstrapper = new DependencyBootstrapper(
            [fixture.Service], NullLogger<DependencyBootstrapper>.Instance);

        var status = (await bootstrapper.ProbeAsync(TestContext.Current.CancellationToken)).Single();

        status.Id.Should().Be("powershell.runtime");
        status.Readiness.Should().Be(DependencyReadiness.Missing);
        bootstrapper.Tasks.Tasks.Should().ContainSingle(task =>
            string.Equals(task.Id, "powershell.runtime", StringComparison.Ordinal)
            && task.State == SetupTaskState.NeedsAction);
        fixture.Processes.VersionCalls.Should().Be(0);
        fixture.Processes.InstallCalls.Should().Be(0);
        fixture.CreateExecutable();
        await bootstrapper.ProbeAsync(TestContext.Current.CancellationToken);
        bootstrapper.Tasks.Tasks.Should().ContainSingle(task =>
            string.Equals(task.Id, "powershell.runtime", StringComparison.Ordinal)
            && task.State == SetupTaskState.Completed);
    }

    [Fact]
    public async Task Existing_healthy_runtime_is_reused_without_installing()
    {
        using var fixture = new Fixture();
        fixture.CreateExecutable();
        fixture.Processes.VersionOutput = "7.6.6";

        await fixture.Service.InstallAsync(TestContext.Current.CancellationToken);

        fixture.Processes.VersionCalls.Should().Be(1);
        fixture.Processes.InstallCalls.Should().Be(0);
        (await fixture.Service.ProbeAsync(TestContext.Current.CancellationToken))
            .Readiness.Should().Be(DependencyReadiness.Ready);
    }

    [Fact]
    public async Task Installation_must_be_verified_before_it_is_reported_ready()
    {
        using var fixture = new Fixture();
        fixture.Processes.OnInstall = fixture.CreateExecutable;
        fixture.Processes.VersionOutput = "7.5.3";

        await fixture.Service.InstallAsync(TestContext.Current.CancellationToken);

        fixture.Processes.InstallCalls.Should().Be(1);
        fixture.Processes.VersionCalls.Should().Be(1);
    }

    [Fact]
    public async Task Installation_that_does_not_restore_the_runtime_fails()
    {
        using var fixture = new Fixture();

        var action = () => fixture.Service.InstallAsync(TestContext.Current.CancellationToken);

        (await action.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*did not pass verification*");
        fixture.Processes.InstallCalls.Should().Be(1);
    }

    [Fact]
    public async Task Corrupted_runtime_is_reported_and_not_treated_as_ready()
    {
        using var fixture = new Fixture();
        fixture.CreateExecutable();
        fixture.Processes.VersionFailure = new TimeoutException("Version probe timed out.");

        var status = await fixture.Service.ProbeAsync(TestContext.Current.CancellationToken);

        status.Readiness.Should().Be(DependencyReadiness.Failed);
        status.Detail.Should().Contain("could not be verified");
    }

    [Theory]
    [InlineData("5.1.0")]
    [InlineData("7.3.9")]
    [InlineData("not a version")]
    public async Task Unsupported_runtime_version_is_not_ready(string output)
    {
        using var fixture = new Fixture();
        fixture.CreateExecutable();
        fixture.Processes.VersionOutput = output;

        var status = await fixture.Service.ProbeAsync(TestContext.Current.CancellationToken);

        status.Readiness.Should().Be(DependencyReadiness.Incompatible);
        fixture.Processes.InstallCalls.Should().Be(0);
    }

    [Fact]
    public async Task Cancelled_install_never_starts_the_package_manager()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = () => fixture.Service.InstallAsync(cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        fixture.Processes.InstallCalls.Should().Be(0);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(
            Path.GetTempPath(), $"kora-pwsh-test-{Guid.NewGuid():N}");
        private readonly string executable;

        public Fixture()
        {
            executable = Path.Combine(directory, "pwsh.exe");
            Processes = new FakeProcesses(executable);
            Service = new WindowsPowerShellSetupService(Processes);
        }

        public FakeProcesses Processes { get; }

        public WindowsPowerShellSetupService Service { get; }

        public void CreateExecutable()
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(executable, "fake executable");
        }

        public void Dispose()
        {
            if (File.Exists(executable))
            {
                File.Delete(executable);
            }
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory);
            }
        }
    }

    private sealed class FakeProcesses(string executable) : IPowerShellProcessRunner
    {
        public string VersionOutput { get; set; } = "7.6.6";

        public Exception? VersionFailure { get; set; }

        public Action? OnInstall { get; set; }

        public int InstallCalls { get; private set; }

        public int VersionCalls { get; private set; }

        public IReadOnlyList<string> GetCandidatePaths() => [executable];

        public Task<string> GetVersionAsync(string path, CancellationToken cancellationToken)
        {
            VersionCalls++;
            if (VersionFailure is not null)
            {
                return Task.FromException<string>(VersionFailure);
            }
            return Task.FromResult(VersionOutput);
        }

        public Task<int> InstallAsync(CancellationToken cancellationToken)
        {
            InstallCalls++;
            OnInstall?.Invoke();
            return Task.FromResult(0);
        }
    }
}
