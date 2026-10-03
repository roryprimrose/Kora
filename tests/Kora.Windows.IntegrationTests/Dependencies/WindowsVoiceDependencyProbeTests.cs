using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Windows.Dependencies;
using Kora.Windows.IntegrationTests.Audio;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed class WindowsVoiceDependencyProbeTests
{
    [Fact]
    public async Task ProbeAsync_honors_cancellation_before_accessing_platform_apis()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var probe = new WindowsVoiceDependencyProbe();

        var action = async () => await probe.ProbeAsync(cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [WindowsFact]
    public async Task ProbeAsync_returns_a_documented_readiness_state()
    {
        var probe = new WindowsVoiceDependencyProbe();

        var result = await probe.ProbeAsync(CancellationToken.None);

        result.Id.Should().Be("windows.voice");
        result.Name.Should().Be("Windows microphone and speech");
        result.Readiness.Should().BeOneOf(
            DependencyReadiness.Missing,
            DependencyReadiness.NeedsConfiguration);
        result.Detail.Should().NotBeNullOrWhiteSpace();
    }
}