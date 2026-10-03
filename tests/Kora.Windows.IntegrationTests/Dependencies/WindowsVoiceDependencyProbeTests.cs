using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Voice;
using Kora.Windows.Dependencies;
using Kora.Windows.IntegrationTests.Audio;

using Neovolve.Logging.Xunit;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed class WindowsVoiceDependencyProbeTests(
    ITestOutputHelper output) : LoggingTestsBase<WindowsVoiceDependencyProbe>(output)
{
    [Fact]
    public async Task ProbeAsync_honors_cancellation_before_accessing_platform_apis()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var probe = CreateProbe(MicrophoneAccessState.Allowed);

        var action = async () => await probe.ProbeAsync(cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [WindowsFact]
    public async Task ProbeAsync_returns_a_documented_readiness_state()
    {
        var probe = CreateProbe(MicrophoneAccessState.Allowed);

        var result = await probe.ProbeAsync(CancellationToken.None);

        result.Id.Should().Be("windows.voice");
        result.Name.Should().Be("Windows microphone and speech");
        result.Readiness.Should().BeOneOf(
            DependencyReadiness.Ready,
            DependencyReadiness.Missing,
            DependencyReadiness.NeedsConfiguration);
        result.Detail.Should().NotBeNullOrWhiteSpace();
    }

    [WindowsFact]
    public async Task ProbeAsync_reports_blocked_microphone_access()
    {
        var probe = CreateProbe(MicrophoneAccessState.Denied);

        var result = await probe.ProbeAsync(CancellationToken.None);

        result.Readiness.Should().Be(DependencyReadiness.NeedsConfiguration);
        result.Detail.Should().Contain("blocked");
    }

    private WindowsVoiceDependencyProbe CreateProbe(MicrophoneAccessState state) =>
        new(
            new StubMicrophoneAccessService(new MicrophoneAccessStatus(
                state,
                state == MicrophoneAccessState.Denied
                    ? "Windows microphone access is blocked."
                    : "Windows microphone access is allowed.")),
            Logger);

    private sealed class StubMicrophoneAccessService(
        MicrophoneAccessStatus status) : IMicrophoneAccessService
    {
        public MicrophoneAccessStatus GetStatus() => status;
    }
}