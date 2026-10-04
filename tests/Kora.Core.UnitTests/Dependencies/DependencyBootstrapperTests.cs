using AwesomeAssertions;

using Kora.Core.Dependencies;

using Neovolve.Logging.Xunit;

namespace Kora.Core.UnitTests.Dependencies;

public sealed class DependencyBootstrapperTests(
    ITestOutputHelper output) : LoggingTestsBase<DependencyBootstrapper>(output)
{
    [Fact]
    public async Task ProbeAsync_returns_probe_results_in_registration_order()
    {
        IDependencyProbe[] probes =
        [
            new StubProbe("storage", DependencyReadiness.Ready),
            new StubProbe("voice", DependencyReadiness.NeedsConfiguration),
        ];
        var bootstrapper = new DependencyBootstrapper(probes, Logger);

        var results = await bootstrapper.ProbeAsync(TestContext.Current.CancellationToken);

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
        var bootstrapper = new DependencyBootstrapper(
            [new StubProbe("storage", DependencyReadiness.Ready)],
            Logger);

        var action = () => bootstrapper.ProbeAsync(cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ProbeAsync_does_not_hide_probe_failures_or_continue()
    {
        var laterProbe = new RecordingProbe();
        var bootstrapper = new DependencyBootstrapper([new ThrowingProbe(), laterProbe], Logger);

        var action = () => bootstrapper.ProbeAsync();

        await action.Should().ThrowAsync<IOException>().WithMessage("probe failed");
        laterProbe.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ProbeAsync_with_no_probes_returns_an_empty_result()
    {
        var bootstrapper = new DependencyBootstrapper([], Logger);

        var results = await bootstrapper.ProbeAsync(TestContext.Current.CancellationToken);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task ProbeAsync_reports_optional_speech_readiness_without_adding_a_setup_task()
    {
        var bootstrapper = new DependencyBootstrapper(
            [new StubProbe("windows.tts", DependencyReadiness.Missing)],
            Logger);

        var results = await bootstrapper.ProbeAsync(TestContext.Current.CancellationToken);

        results.Should().ContainSingle().Which.Id.Should().Be("windows.tts");
        bootstrapper.Tasks.Tasks.Should().BeEmpty();
    }

    [Fact]
    public async Task ProbeAsync_reconciles_missing_and_recovered_dependencies_on_each_run()
    {
        var probe = new MutableProbe();
        var bootstrapper = new DependencyBootstrapper([probe], Logger);

        await bootstrapper.ProbeAsync(TestContext.Current.CancellationToken);
        bootstrapper.Tasks.Tasks.Should().ContainSingle()
            .Which.State.Should().Be(SetupTaskState.NeedsAction);

        probe.Readiness = DependencyReadiness.Ready;
        await bootstrapper.ProbeAsync(TestContext.Current.CancellationToken);
        bootstrapper.Tasks.Tasks.Should().ContainSingle()
            .Which.State.Should().Be(SetupTaskState.Completed);

        probe.Readiness = DependencyReadiness.Failed;
        await bootstrapper.ProbeAsync(TestContext.Current.CancellationToken);
        bootstrapper.Tasks.Tasks.Should().ContainSingle()
            .Which.State.Should().Be(SetupTaskState.Failed);
    }

    private sealed class MutableProbe : ISetupDependencyProbe
    {
        public DependencyReadiness Readiness { get; set; } = DependencyReadiness.Missing;

        public string TaskId => "sqlite";

        public string TaskName => "SQLite";

        public ValueTask<DependencyStatus> ProbeAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new DependencyStatus("sqlite", "SQLite", Readiness, "probe result"));
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