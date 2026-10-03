using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Windows.Dependencies;
using Kora.Windows.IntegrationTests.Audio;

using Neovolve.Logging.Xunit;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed class WindowsTextToSpeechDependencyProbeTests(
    ITestOutputHelper output) : LoggingTestsBase<WindowsTextToSpeechDependencyProbe>(output)
{
    [Fact]
    public async Task ProbeAsync_honors_cancellation_before_accessing_platform_apis()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var probe = new WindowsTextToSpeechDependencyProbe(Logger);

        var action = async () => await probe.ProbeAsync(cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [WindowsFact]
    public async Task ProbeAsync_returns_a_documented_readiness_state()
    {
        var probe = new WindowsTextToSpeechDependencyProbe(Logger);

        var result = await probe.ProbeAsync(CancellationToken.None);

        result.Id.Should().Be("windows.tts");
        result.Name.Should().Be("Windows text-to-speech");
        result.Readiness.Should().BeOneOf(
            DependencyReadiness.Ready,
            DependencyReadiness.Missing,
            DependencyReadiness.NeedsConfiguration);
        result.Detail.Should().NotBeNullOrWhiteSpace();
    }
}