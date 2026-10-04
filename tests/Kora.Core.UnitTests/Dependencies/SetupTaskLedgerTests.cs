using AwesomeAssertions;

using Kora.Core.Dependencies;

namespace Kora.Core.UnitTests.Dependencies;

public sealed class SetupTaskLedgerTests
{
    [Fact]
    public void Start_updates_active_task_and_prevents_concurrent_setup()
    {
        var ledger = new SetupTaskLedger();
        var changes = 0;
        ledger.Changed += (_, _) => changes++;

        ledger.ActiveTask.Should().BeNull();
        ledger.Start("storage", "Storage", "Checking");
        ledger.ActiveTask.Should().Be(new SetupTask("storage", "Storage", SetupTaskState.Running, "Checking"));
        var startAnother = () => ledger.Start("sqlite", "SQLite", "Checking");
        startAnother.Should().Throw<InvalidOperationException>().WithMessage("*already running*");
        ledger.Tasks.Should().ContainSingle();
        changes.Should().Be(1);

        ledger.Update("storage", SetupTaskState.Completed, "Ready", 100);
        ledger.ActiveTask.Should().BeNull();
        ledger.Tasks.Should().ContainSingle().Which.ProgressPercentage.Should().Be(100);
        changes.Should().Be(2);
        ledger.Start("sqlite", "SQLite", "Checking");
        ledger.ActiveTask?.Id.Should().Be("sqlite");
        changes.Should().Be(3);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Update_rejects_out_of_range_progress(int progress)
    {
        var ledger = new SetupTaskLedger();
        ledger.Start("sqlite", "SQLite", "Checking");

        var update = () => ledger.Update("sqlite", SetupTaskState.Running, "Still checking", progress);

        update.Should().Throw<ArgumentOutOfRangeException>();
        ledger.ActiveTask?.Detail.Should().Be("Checking");
    }

    [Fact]
    public void Update_requires_an_existing_task_and_accepts_boundary_progress()
    {
        var ledger = new SetupTaskLedger();
        var missing = () => ledger.Update("absent", SetupTaskState.Completed, "Ready");
        missing.Should().Throw<InvalidOperationException>().WithMessage("*absent*");

        ledger.Start("sqlite", "SQLite", "Checking");
        ledger.Update("sqlite", SetupTaskState.Running, "Starting", 0);
        ledger.Tasks.Single().ProgressPercentage.Should().Be(0);
        ledger.Update("sqlite", SetupTaskState.Completed, "Ready");
        ledger.Tasks.Single().ProgressPercentage.Should().BeNull();
    }

    [Theory]
    [InlineData(DependencyReadiness.Ready, SetupTaskState.Completed)]
    [InlineData(DependencyReadiness.Failed, SetupTaskState.Failed)]
    [InlineData(DependencyReadiness.Missing, SetupTaskState.NeedsAction)]
    [InlineData(DependencyReadiness.Blocked, SetupTaskState.NeedsAction)]
    public void Reconcile_maps_readiness_and_keeps_running_tasks(
        DependencyReadiness readiness,
        SetupTaskState expected)
    {
        var ledger = new SetupTaskLedger();
        var changes = 0;
        ledger.Changed += (_, _) => changes++;
        var status = new DependencyStatus("sqlite", "SQLite", readiness, "Probe result");

        ledger.Reconcile(status);
        ledger.Tasks.Should().ContainSingle().Which.State.Should().Be(expected);
        changes.Should().Be(1);

        ledger.Start("sqlite", "SQLite", "Installing");
        ledger.Reconcile(status);
        ledger.Tasks.Single().Should().Be(new SetupTask("sqlite", "SQLite", SetupTaskState.Running, "Installing"));
        changes.Should().Be(2);

        ledger.Update("sqlite", SetupTaskState.Cancelled, "Cancelled");
        ledger.Reconcile(status);
        ledger.Tasks.Single().Should().Be(new SetupTask("sqlite", "SQLite", expected, "Probe result"));
        changes.Should().Be(4);
    }

    [Fact]
    public void Remove_only_notifies_when_a_task_existed()
    {
        var ledger = new SetupTaskLedger();
        var changes = 0;
        ledger.Changed += (_, _) => changes++;
        ledger.Remove("absent");
        changes.Should().Be(0);

        ledger.Reconcile(new DependencyStatus("sqlite", "SQLite", DependencyReadiness.Missing, "Missing"));
        ledger.Remove("sqlite");
        ledger.Remove("sqlite");
        ledger.Tasks.Should().BeEmpty();
        changes.Should().Be(2);
    }

    [Fact]
    public void Reconcile_and_remove_work_without_subscribers()
    {
        var ledger = new SetupTaskLedger();

        ledger.Reconcile(new DependencyStatus("sqlite", "SQLite", DependencyReadiness.Ready, "Ready"));
        ledger.Remove("sqlite");

        ledger.Tasks.Should().BeEmpty();
    }
}
