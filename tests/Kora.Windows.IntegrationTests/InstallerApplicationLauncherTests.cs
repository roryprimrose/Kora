using AwesomeAssertions;
using Kora.Setup;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed class InstallerApplicationLauncherTests
{
    private static readonly string ExeHash = new('A', 64);
    private static readonly string DllHash = new('B', 64);

    [Theory]
    [InlineData(InstallScope.CurrentUser)]
    [InlineData(InstallScope.AllUsers)]
    public async Task ExactCandidateIsStartedInItsSelectedScopeWithoutShellOrElevation(InstallScope scope)
    {
        var platform = new Platform();
        await Launcher(platform).LaunchAsync(scope, TestContext.Current.CancellationToken);
        platform.Scope.Should().Be(scope);
        platform.Version.Should().Be("0.1.0");
        platform.Reads.Should().Equal(@"C:\installed\Kora.exe", @"C:\installed\Kora.dll");
        platform.Starts.Should().Equal(@"C:\installed\Kora.exe");
    }

    [Fact]
    public async Task ElevatedOrNonInteractiveContextCannotReadOrStartApplication()
    {
        var platform = new Platform { Eligible = false };
        var launch = () => Launcher(platform).LaunchAsync(InstallScope.CurrentUser, TestContext.Current.CancellationToken);
        await launch.Should().ThrowAsync<InvalidOperationException>();
        platform.Reads.Should().BeEmpty();
        platform.Starts.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EitherMismatchedInstalledFileBlocksLaunch(bool executable)
    {
        var platform = new Platform { WrongExe = executable, WrongDll = !executable };
        var launch = () => Launcher(platform).LaunchAsync(InstallScope.CurrentUser, TestContext.Current.CancellationToken);
        await launch.Should().ThrowAsync<InvalidDataException>();
        platform.Starts.Should().BeEmpty();
    }

    [Fact]
    public async Task MissingInstalledFileIsExplicitAndNeverFallsBackToAnotherPath()
    {
        var platform = new Platform { Failure = new FileNotFoundException("Missing.") };
        var launch = () => Launcher(platform).LaunchAsync(InstallScope.CurrentUser, TestContext.Current.CancellationToken);
        await launch.Should().ThrowAsync<FileNotFoundException>();
        platform.Starts.Should().BeEmpty();
        platform.Reads.Should().ContainSingle();
    }

    [Fact]
    public async Task CancellationBeforeLaunchDoesNotStartAProcess()
    {
        var platform = new Platform();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var launch = () => Launcher(platform).LaunchAsync(InstallScope.CurrentUser, cancellation.Token);
        await launch.Should().ThrowAsync<OperationCanceledException>();
        platform.Starts.Should().BeEmpty();
    }

    [Fact]
    public async Task ChangedLaunchContextAfterVerificationFailsClosed()
    {
        var platform = new Platform { LoseEligibility = true };
        var launch = () => Launcher(platform).LaunchAsync(InstallScope.CurrentUser, TestContext.Current.CancellationToken);
        await launch.Should().ThrowAsync<InvalidOperationException>();
        platform.Starts.Should().BeEmpty();
    }

    [Fact]
    public async Task CancellationAfterVerificationDoesNotStartAProcess()
    {
        using var cancellation = new CancellationTokenSource();
        var platform = new Platform { CancelAfterVerification = cancellation };
        var launch = () => Launcher(platform).LaunchAsync(InstallScope.CurrentUser, cancellation.Token);
        await launch.Should().ThrowAsync<OperationCanceledException>();
        platform.Reads.Should().HaveCount(2);
        platform.Starts.Should().BeEmpty();
    }

    [Fact]
    public async Task UnknownScopeDoesNotResolveOrStartApplication()
    {
        var platform = new Platform();
        var launch = () => Launcher(platform).LaunchAsync((InstallScope)99, TestContext.Current.CancellationToken);
        await launch.Should().ThrowAsync<ArgumentOutOfRangeException>();
        platform.Reads.Should().BeEmpty();
        platform.Starts.Should().BeEmpty();
    }

    [Fact]
    public async Task InvalidCandidateHashBlocksLaunchBeforeFileIo()
    {
        var platform = new Platform();
        var launcher = new InstallerApplicationLauncher(platform, "0.1.0", "invalid", DllHash,
            NullLogger<InstallerApplicationLauncher>.Instance);
        var launch = () => launcher.LaunchAsync(InstallScope.CurrentUser, TestContext.Current.CancellationToken);
        await launch.Should().ThrowAsync<InvalidDataException>();
        platform.Reads.Should().BeEmpty();
        platform.Starts.Should().BeEmpty();
    }

    private static InstallerApplicationLauncher Launcher(Platform platform) =>
        new(platform, "0.1.0", ExeHash, DllHash, NullLogger<InstallerApplicationLauncher>.Instance);

    private sealed class Platform : IInstallerLaunchPlatform
    {
        public bool Eligible { get; init; } = true;
        public bool LoseEligibility { get; init; }
        public bool WrongExe { get; init; }
        public bool WrongDll { get; init; }
        public Exception? Failure { get; init; }
        public CancellationTokenSource? CancelAfterVerification { get; init; }
        public List<string> Reads { get; } = [];
        public List<string> Starts { get; } = [];
        public InstallScope Scope { get; private set; }
        public string? Version { get; private set; }
        public bool IsInteractiveNonElevated => Eligible && !(LoseEligibility && Reads.Count == 2);

        public string ExecutablePath(InstallScope scope, string productVersion)
        {
            Scope = scope;
            Version = productVersion;
            return @"C:\installed\Kora.exe";
        }

        public Task<string> Sha256Async(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads.Add(path);
            if (Reads.Count == 2) { CancelAfterVerification?.Cancel(); }
            if (Failure is not null) { return Task.FromException<string>(Failure); }
            return Task.FromResult(path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? WrongExe ? new string('C', 64) : ExeHash
                : WrongDll ? new string('C', 64) : DllHash);
        }

        public void Start(string executable) => Starts.Add(executable);
    }
}
