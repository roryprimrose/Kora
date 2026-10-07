using AwesomeAssertions;

using Kora.Core.Dependencies;

using Neovolve.Logging.Xunit;

namespace Kora.Core.UnitTests.Dependencies;

public sealed class DependencyBootstrapperTests(
    ITestOutputHelper output) : LoggingTestsBase<DependencyBootstrapper>(output)
{
    [Fact]
    public async Task Observations_are_per_host_immutable_and_replaced_by_actual_completed_checks()
    {
        using var bootstrapper = new DependencyBootstrapper([new StubProbe("storage", DependencyReadiness.Ready)], Logger);
        using var otherHost = new DependencyBootstrapper([], Logger, TimeProvider.System);
        bootstrapper.RecordObservation(new("local.inference", "runtime", DependencyReadiness.Ready, "observed"));
        var prior = bootstrapper.Observations;
        bootstrapper.RecordObservation(new("local.inference", "runtime", DependencyReadiness.Failed, "failed"));
        prior.Single().Status.Readiness.Should().Be(DependencyReadiness.Ready);
        bootstrapper.Observations.Single().Status.Readiness.Should().Be(DependencyReadiness.Failed);
        otherHost.Observations.Should().BeEmpty();
        await bootstrapper.ProbeAsync(TestContext.Current.CancellationToken);
        bootstrapper.Observations.Single().Status.Id.Should().Be("storage");
        bootstrapper.Observations.Single().ObservedAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
        var invalid = () => bootstrapper.RecordObservation(new("storage", "Storage", (DependencyReadiness)99, "invalid"));
        invalid.Should().Throw<ArgumentOutOfRangeException>();
        var missing = () => bootstrapper.RecordObservation(null!);
        missing.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Interrupted_probe_does_not_retain_a_previously_ready_runtime()
    {
        using var bootstrapper = new DependencyBootstrapper([new ThrowingProbe()], Logger);
        bootstrapper.RecordObservation(new("local.inference", "runtime", DependencyReadiness.Ready, "ready"));
        var check = () => bootstrapper.ProbeAsync(TestContext.Current.CancellationToken);
        await check.Should().ThrowAsync<IOException>();
        bootstrapper.Observations.Should().BeEmpty();
    }

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

    [Theory]
    [InlineData(DependencyReadiness.Ready, SetupTaskState.Completed)]
    [InlineData(DependencyReadiness.Failed, SetupTaskState.Failed)]
    [InlineData(DependencyReadiness.Missing, SetupTaskState.NeedsAction)]
    public async Task ProbeAsync_tracks_the_setup_probes_final_state(
        DependencyReadiness readiness, SetupTaskState expected)
    {
        var probe = new MutableProbe { Readiness = readiness };
        using var bootstrapper = new DependencyBootstrapper([probe], Logger);

        var result = await bootstrapper.ProbeAsync(TestContext.Current.CancellationToken);

        result.Should().ContainSingle().Which.Readiness.Should().Be(readiness);
        bootstrapper.Tasks.Tasks.Should().ContainSingle().Which.State.Should().Be(expected);
    }

    [Theory]
    [InlineData(false, SetupTaskState.Failed)]
    [InlineData(true, SetupTaskState.Cancelled)]
    public async Task ProbeAsync_records_setup_probe_failures_and_cancellation(
        bool cancel, SetupTaskState expected)
    {
        using var bootstrapper = new DependencyBootstrapper([new ThrowingSetupProbe(cancel)], Logger);
        var action = () => bootstrapper.ProbeAsync(TestContext.Current.CancellationToken);

        if (cancel)
        {
            await action.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            await action.Should().ThrowAsync<IOException>().WithMessage("setup failed");
        }
        bootstrapper.Tasks.Tasks.Should().ContainSingle().Which.State.Should().Be(expected);
    }

    [Fact]
    public async Task ProbeAsync_serializes_concurrent_checks()
    {
        var probe = new BlockingProbe();
        using var bootstrapper = new DependencyBootstrapper([probe], Logger);
        var first = bootstrapper.ProbeAsync(TestContext.Current.CancellationToken);
        await probe.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var second = bootstrapper.ProbeAsync(TestContext.Current.CancellationToken);
        probe.Calls.Should().Be(1);

        probe.Release.SetResult();
        await Task.WhenAll(first, second);

        probe.Calls.Should().Be(2);
    }

    private sealed class BlockingProbe : IDependencyProbe
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }

        public async ValueTask<DependencyStatus> ProbeAsync(CancellationToken cancellationToken)
        {
            Calls++;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new DependencyStatus("storage", "Storage", DependencyReadiness.Ready, "Ready");
        }
    }

    private sealed class ThrowingSetupProbe(bool cancel) : ISetupDependencyProbe
    {
        public string TaskId => "setup";
        public string TaskName => "Setup";

        public ValueTask<DependencyStatus> ProbeAsync(CancellationToken cancellationToken) =>
            cancel
                ? ValueTask.FromException<DependencyStatus>(new OperationCanceledException())
                : ValueTask.FromException<DependencyStatus>(new IOException("setup failed"));
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