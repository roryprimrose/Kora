using AwesomeAssertions;

using Kora.Core.Dependencies;

using Microsoft.Extensions.Logging;

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

    [Theory]
    [InlineData(DependencyReadiness.Ready, false, false)]
    [InlineData(DependencyReadiness.Missing, false, false)]
    [InlineData(DependencyReadiness.Failed, false, false)]
    [InlineData(DependencyReadiness.Ready, true, false)]
    [InlineData(DependencyReadiness.Missing, true, false)]
    [InlineData(DependencyReadiness.Failed, true, false)]
    [InlineData(DependencyReadiness.Ready, false, true)]
    [InlineData(DependencyReadiness.Missing, false, true)]
    [InlineData(DependencyReadiness.Failed, false, true)]
    [InlineData(DependencyReadiness.Ready, true, true)]
    [InlineData(DependencyReadiness.Missing, true, true)]
    [InlineData(DependencyReadiness.Failed, true, true)]
    public async Task Cancelled_normal_probe_completion_is_joined_without_publishing_or_continuing(
        DependencyReadiness readiness, bool tracked, bool withinSetup)
    {
        using var cancellation = new CancellationTokenSource();
        HeldCompletionProbe probe = tracked ? new HeldSetupCompletionProbe() : new HeldCompletionProbe();
        probe.Readiness = readiness;
        var later = new RecordingProbe();
        using var bootstrapper = new DependencyBootstrapper(
            [new StubProbe("storage", DependencyReadiness.Ready), probe, later], Logger);
        bootstrapper.RecordObservation(new("local.inference", "Runtime", DependencyReadiness.Ready, "Old check."));
        if (withinSetup)
        {
            bootstrapper.Tasks.Start("installation", "Install", "Verifying.");
        }
        var check = withinSetup
            ? bootstrapper.ProbeWithinSetupAsync(bootstrapper.Tasks.ActiveTask!, cancellation.Token)
            : bootstrapper.ProbeAsync(cancellation.Token);
        try
        {
            await probe.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            await cancellation.CancelAsync();

            check.IsCompleted.Should().BeFalse("cancellation must not abandon the outstanding probe");
            probe.Exited.Should().BeFalse();
            later.WasCalled.Should().BeFalse();
        }
        finally
        {
            probe.Release.TrySetResult();
            var complete = () => check;
            await complete.Should().ThrowAsync<OperationCanceledException>();
        }

        probe.Exited.Should().BeTrue();
        later.WasCalled.Should().BeFalse();
        bootstrapper.Observations.Should().ContainSingle().Which.Status.Id.Should().Be("storage");
        if (withinSetup)
        {
            bootstrapper.Tasks.Tasks.Should().ContainSingle().Which.State.Should().Be(SetupTaskState.Running);
            bootstrapper.Tasks.Update("installation", SetupTaskState.Cancelled, "Cancelled by owner.");
        }
        else if (tracked)
        {
            bootstrapper.Tasks.Tasks.Should().ContainSingle().Which.State.Should().Be(SetupTaskState.Cancelled);
        }
        else
        {
            bootstrapper.Tasks.Tasks.Should().BeEmpty();
        }
        bootstrapper.Tasks.ActiveTask.Should().BeNull();

        probe.Readiness = DependencyReadiness.Ready;
        var fresh = await bootstrapper.ProbeAsync(TestContext.Current.CancellationToken);
        fresh.Should().HaveCount(3);
        bootstrapper.Observations.Should().Contain(item =>
            item.Status.Id == "local.inference" && item.Status.Readiness == DependencyReadiness.Ready);
        later.WasCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Parent_verification_preserves_order_observations_and_the_original_running_task()
    {
        var tracked = new MutableProbe { Readiness = DependencyReadiness.Ready };
        using var bootstrapper = new DependencyBootstrapper(
            [new StubProbe("storage", DependencyReadiness.Ready), tracked], Logger);
        bootstrapper.Tasks.Start("installation", "Install", "Verifying.");
        var parent = bootstrapper.Tasks.ActiveTask!;
        var transitions = new List<SetupTask>();
        bootstrapper.Tasks.Changed += (_, _) => transitions.AddRange(bootstrapper.Tasks.Tasks);

        var results = await bootstrapper.ProbeWithinSetupAsync(parent, TestContext.Current.CancellationToken);

        results.Select(item => item.Id).Should().Equal("storage", "sqlite");
        bootstrapper.Observations.Select(item => item.Status).Should().Equal(results);
        bootstrapper.Tasks.Tasks.Should().ContainSingle().Which.Should().BeSameAs(parent);
        transitions.Should().BeEmpty();

        bootstrapper.Tasks.Update(parent.Id, SetupTaskState.Completed, "Installed.");
        await bootstrapper.ProbeAsync(TestContext.Current.CancellationToken);
        bootstrapper.Tasks.Tasks.Should().HaveCount(2);
        bootstrapper.Tasks.Tasks.Single(task =>
            string.Equals(task.Id, tracked.TaskId, StringComparison.Ordinal)).State.Should().Be(SetupTaskState.Completed);
    }

    [Fact]
    public async Task Parent_verification_requires_the_current_ledger_record()
    {
        using var bootstrapper = new DependencyBootstrapper([], Logger);
        using var other = new DependencyBootstrapper([], Logger);
        var missing = () => bootstrapper.ProbeWithinSetupAsync(null!);
        await missing.Should().ThrowAsync<ArgumentNullException>();
        bootstrapper.Tasks.Start("installation", "Install", "Verifying.");
        other.Tasks.Start("installation", "Install", "Verifying.");
        var wrongHost = () => bootstrapper.ProbeWithinSetupAsync(other.Tasks.ActiveTask!);
        await wrongHost.Should().ThrowAsync<InvalidOperationException>();
        var parent = bootstrapper.Tasks.ActiveTask!;
        var forged = () => bootstrapper.ProbeWithinSetupAsync(parent with { });
        await forged.Should().ThrowAsync<InvalidOperationException>();
        bootstrapper.Tasks.Update(parent.Id, SetupTaskState.Completed, "Done.");
        var stale = () => bootstrapper.ProbeWithinSetupAsync(parent);
        await stale.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Parent_verification_rechecks_ownership_after_waiting_for_the_probe_gate()
    {
        var probe = new BlockingProbe();
        using var bootstrapper = new DependencyBootstrapper([probe], Logger);
        var first = bootstrapper.ProbeAsync(TestContext.Current.CancellationToken);
        await probe.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        bootstrapper.Tasks.Start("installation", "Install", "Verifying.");
        var queued = bootstrapper.ProbeWithinSetupAsync(bootstrapper.Tasks.ActiveTask!,
            TestContext.Current.CancellationToken);
        bootstrapper.Tasks.Update("installation", SetupTaskState.Cancelled, "Retired.");
        probe.Release.SetResult();
        await first;

        var check = () => queued;
        await check.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("The setup task no longer owns verification.");
        probe.Calls.Should().Be(1);
        bootstrapper.Observations.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Parent_verification_joins_retired_ownership_without_publishing_or_changing_terminal_state(
        bool replace, bool afterLogging)
    {
        var probe = new HeldSetupCompletionProbe { Readiness = DependencyReadiness.Ready };
        DependencyBootstrapper? host = null;
        void Retire()
        {
            host!.Tasks.Update("installation", SetupTaskState.Cancelled, "Retired.");
            if (replace)
            {
                host.Tasks.Start("another", "Another install", "Running.");
            }
        }
        using var bootstrapper = new DependencyBootstrapper([probe, new RecordingProbe()],
            new CallbackLogger(id => { if (afterLogging && id == 2) { Retire(); } }));
        host = bootstrapper;
        bootstrapper.Tasks.Start("installation", "Install", "Verifying.");
        var check = bootstrapper.ProbeWithinSetupAsync(bootstrapper.Tasks.ActiveTask!,
            TestContext.Current.CancellationToken);
        await probe.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        if (!afterLogging)
        {
            Retire();
        }
        check.IsCompleted.Should().BeFalse();
        probe.Release.SetResult();
        var complete = () => check;

        await complete.Should().ThrowAsync<InvalidOperationException>();
        probe.Exited.Should().BeTrue();
        bootstrapper.Observations.Should().BeEmpty();
        bootstrapper.Tasks.Tasks.Single(task =>
            string.Equals(task.Id, "installation", StringComparison.Ordinal)).State.Should().Be(SetupTaskState.Cancelled);
        bootstrapper.Tasks.Tasks.Should().NotContain(task =>
            string.Equals(task.Id, probe.TaskId, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_between_probe_return_and_observation_prevents_publication(bool withinSetup)
    {
        using var cancellation = new CancellationTokenSource();
        var later = new RecordingProbe();
        using var bootstrapper = new DependencyBootstrapper(
            [new MutableProbe { Readiness = DependencyReadiness.Ready }, later],
            new CallbackLogger(id => { if (id == 2) { cancellation.Cancel(); } }));
        if (withinSetup)
        {
            bootstrapper.Tasks.Start("installation", "Install", "Verifying.");
        }
        var check = () => withinSetup
            ? bootstrapper.ProbeWithinSetupAsync(bootstrapper.Tasks.ActiveTask!, cancellation.Token)
            : bootstrapper.ProbeAsync(cancellation.Token);

        await check.Should().ThrowAsync<OperationCanceledException>();

        bootstrapper.Observations.Should().BeEmpty();
        later.WasCalled.Should().BeFalse();
        bootstrapper.Tasks.Tasks.Should().ContainSingle().Which.State.Should()
            .Be(withinSetup ? SetupTaskState.Running : SetupTaskState.Cancelled);
    }

    private sealed class CallbackLogger(Action<int> callback) : ILogger<DependencyBootstrapper>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) => callback(eventId.Id);
    }

    private class HeldCompletionProbe : IDependencyProbe
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DependencyReadiness Readiness { get; set; }
        public bool Exited { get; private set; }

        public async ValueTask<DependencyStatus> ProbeAsync(CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task;
            Exited = true;
            return new("local.inference", "Local model", Readiness, "Final check.");
        }
    }

    private sealed class HeldSetupCompletionProbe : HeldCompletionProbe, ISetupDependencyProbe
    {
        public string TaskId => "local.inference";
        public string TaskName => "Local model";
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