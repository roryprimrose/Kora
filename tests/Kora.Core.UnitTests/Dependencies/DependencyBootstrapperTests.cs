using AwesomeAssertions;

using Kora.Core.Dependencies;

namespace Kora.Core.UnitTests.Dependencies;

public sealed class DependencyBootstrapperTests
{
    [Fact]
    public async Task ProbeAsync_returns_probe_results_in_registration_order()
    {
        IDependencyProbe[] probes =
        [
            new StubProbe("storage", DependencyReadiness.Ready),
            new StubProbe("voice", DependencyReadiness.NeedsConfiguration),
        ];
        var bootstrapper = new DependencyBootstrapper(probes);

        var results = await bootstrapper.ProbeAsync();

        results.Select(result => result.Id).Should().ContainInOrder("storage", "voice");
        results.Select(result => result.Readiness)
            .Should()
            .ContainInOrder(DependencyReadiness.Ready, DependencyReadiness.NeedsConfiguration);
    }

    [Fact]
    public async Task ProbeAsync_honors_cancellation_before_a_probe_runs()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var bootstrapper = new DependencyBootstrapper([new StubProbe("storage", DependencyReadiness.Ready)]);

        var action = () => bootstrapper.ProbeAsync(cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ProbeAsync_does_not_hide_probe_failures_or_continue()
    {
        var laterProbe = new RecordingProbe();
        var bootstrapper = new DependencyBootstrapper([new ThrowingProbe(), laterProbe]);

        var action = () => bootstrapper.ProbeAsync();

        await action.Should().ThrowAsync<IOException>().WithMessage("probe failed");
        laterProbe.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ProbeAsync_with_no_probes_returns_an_empty_result()
    {
        var bootstrapper = new DependencyBootstrapper([]);

        var results = await bootstrapper.ProbeAsync();

        results.Should().BeEmpty();
    }

    private sealed class StubProbe(string id, DependencyReadiness readiness) : IDependencyProbe
    {
        public ValueTask<DependencyStatus> ProbeAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new DependencyStatus(id, id, readiness, $"{id} detail"));
    }

    private sealed class ThrowingProbe : IDependencyProbe
    {
        public ValueTask<DependencyStatus> ProbeAsync(CancellationToken cancellationToken) =>
            ValueTask.FromException<DependencyStatus>(new IOException("probe failed"));
    }

    private sealed class RecordingProbe : IDependencyProbe
    {
        public bool WasCalled { get; private set; }

        public ValueTask<DependencyStatus> ProbeAsync(CancellationToken cancellationToken)
        {
            WasCalled = true;
            return ValueTask.FromResult(new DependencyStatus("later", "later", DependencyReadiness.Ready, "ready"));
        }
    }
}