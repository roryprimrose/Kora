using AwesomeAssertions;

using Kora.Core.Dependencies;

using Neovolve.Logging.Xunit;

namespace Kora.Core.UnitTests.Dependencies;

public sealed class StorageDependencyProbeTests(ITestOutputHelper output) : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "Kora.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void ApplicationDataPaths_resolves_Kora_specific_absolute_roots()
    {
        var paths = new ApplicationDataPaths();

        Path.IsPathFullyQualified(paths.LocalRoot).Should().BeTrue();
        Path.IsPathFullyQualified(paths.RoamingRoot).Should().BeTrue();
        Path.GetFileName(paths.LocalRoot).Should().Be("Kora");
        Path.GetFileName(paths.RoamingRoot).Should().Be("Kora");
    }

    [Fact]
    public async Task ProbeAsync_creates_required_application_directories()
    {
        var paths = new TestPaths(
            Path.Combine(root, "Local"),
            Path.Combine(root, "Roaming"));
        using var logger = output.BuildLoggerFor<StorageDependencyProbe>();
        var probe = new StorageDependencyProbe(paths, logger);

        probe.TaskId.Should().Be("kora.storage");
        probe.TaskName.Should().Be("Application storage");
        var result = await probe.ProbeAsync(CancellationToken.None);

        result.Readiness.Should().Be(DependencyReadiness.Ready);
        result.Detail.Should().Contain(paths.LocalRoot);
        Directory.Exists(paths.LocalRoot).Should().BeTrue();
        Directory.Exists(Path.Combine(paths.LocalRoot, "Logs")).Should().BeTrue();
        Directory.Exists(Path.Combine(paths.RoamingRoot, "Skills")).Should().BeTrue();
    }

    [Fact]
    public async Task ProbeAsync_is_idempotent()
    {
        var paths = new TestPaths(
            Path.Combine(root, "Local"),
            Path.Combine(root, "Roaming"));
        using var logger = output.BuildLoggerFor<StorageDependencyProbe>();
        var probe = new StorageDependencyProbe(paths, logger);

        await probe.ProbeAsync(CancellationToken.None);
        var secondResult = await probe.ProbeAsync(CancellationToken.None);

        secondResult.Readiness.Should().Be(DependencyReadiness.Ready);
    }

    [Fact]
    public async Task ProbeAsync_honors_pre_cancelled_token_without_creating_directories()
    {
        var paths = new TestPaths(
            Path.Combine(root, "Local"),
            Path.Combine(root, "Roaming"));
        using var logger = output.BuildLoggerFor<StorageDependencyProbe>();
        var probe = new StorageDependencyProbe(paths, logger);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = async () => await probe.ProbeAsync(cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(root).Should().BeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed record TestPaths(string LocalRoot, string RoamingRoot) : IApplicationDataPaths;
}