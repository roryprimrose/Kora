using AwesomeAssertions;

using Kora.Application.Dependencies;
using Kora.Core.Dependencies;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Dependencies;

public sealed class DependencySetupWorkflowTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Original_setup_remains_running_until_all_final_verification_finishes(bool cancel)
    {
        var probe = new FinalProbe(DependencyReadiness.Ready);
        using var bootstrapper = new DependencyBootstrapper([probe], NullLogger<DependencyBootstrapper>.Instance);
        var transitions = new List<SetupTask>();
        bootstrapper.Tasks.Changed += (_, _) => transitions.AddRange(bootstrapper.Tasks.Tasks);
        var workflow = new DependencySetupWorkflow(bootstrapper, new Installer(), new PowerShellSetup());
        using var cancellation = new CancellationTokenSource();
        var install = workflow.InstallLocalModelAsync(new InlineProgress(), cancellation.Token);
        try
        {
            await probe.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            bootstrapper.Tasks.Tasks.Should().ContainSingle()
                .Which.State.Should().Be(SetupTaskState.Running);
            transitions.Should().OnlyContain(task => task.State == SetupTaskState.Running);
            bootstrapper.Tasks.ActiveTask!.Detail.Should().Be("Validating local inference readiness.");
            if (cancel)
            {
                await cancellation.CancelAsync();
            }
            install.IsCompleted.Should().BeFalse();
        }
        finally
        {
            if (cancel)
            {
                await cancellation.CancelAsync();
            }
            probe.Release.TrySetResult();
            if (cancel)
            {
                var complete = () => install;
                await complete.Should().ThrowAsync<OperationCanceledException>();
            }
            else
            {
                await install;
            }
        }

        transitions.Count(task => task.State != SetupTaskState.Running).Should().Be(1);
        bootstrapper.Tasks.Tasks.Should().ContainSingle().Which.State.Should()
            .Be(cancel ? SetupTaskState.Cancelled : SetupTaskState.Completed);
    }

    [Theory]
    [InlineData(DependencyReadiness.Ready)]
    [InlineData(DependencyReadiness.Missing)]
    [InlineData(DependencyReadiness.Failed)]
    public async Task Cancellation_during_final_verification_joins_the_probe_and_cancels_the_original_setup(
        DependencyReadiness readiness)
    {
        var probe = new FinalProbe(readiness);
        var installer = new Installer();
        using var bootstrapper = new DependencyBootstrapper([probe], NullLogger<DependencyBootstrapper>.Instance);
        var workflow = new DependencySetupWorkflow(bootstrapper, installer, new PowerShellSetup());
        using var cancellation = new CancellationTokenSource();
        var install = workflow.InstallLocalModelAsync(new InlineProgress(), cancellation.Token);
        try
        {
            await probe.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            installer.Calls.Should().Be(1);
            installer.Token.Should().Be(cancellation.Token);
            probe.Token.Should().Be(cancellation.Token);
            await cancellation.CancelAsync();
            install.IsCompleted.Should().BeFalse();
            probe.Exited.Should().BeFalse();
        }
        finally
        {
            probe.Release.TrySetResult();
            var complete = () => install;
            await complete.Should().ThrowAsync<OperationCanceledException>();
        }

        probe.Exited.Should().BeTrue();
        probe.Calls.Should().Be(1);
        installer.Calls.Should().Be(1);
        bootstrapper.Observations.Should().BeEmpty();
        bootstrapper.Tasks.Tasks.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new SetupTask(DependencySetupWorkflow.LocalModelTaskId, probe.TaskName,
                SetupTaskState.Cancelled, "Local model setup was cancelled."));
        bootstrapper.Tasks.ActiveTask.Should().BeNull();
    }

    [Fact]
    public async Task Cancellation_after_installation_does_not_start_or_publish_final_verification()
    {
        var probe = new FinalProbe(DependencyReadiness.Ready);
        probe.Release.SetResult();
        using var bootstrapper = new DependencyBootstrapper([probe], NullLogger<DependencyBootstrapper>.Instance);
        using var cancellation = new CancellationTokenSource();
        bootstrapper.Tasks.Changed += (_, _) =>
        {
            if (bootstrapper.Tasks.Tasks.Any(task =>
                task.State == SetupTaskState.Running
                && string.Equals(task.Detail, "Validating local inference readiness.", StringComparison.Ordinal)))
            {
                cancellation.Cancel();
            }
        };
        var workflow = new DependencySetupWorkflow(bootstrapper, new Installer(), new PowerShellSetup());

        var install = () => workflow.InstallLocalModelAsync(new InlineProgress(), cancellation.Token);

        await install.Should().ThrowAsync<OperationCanceledException>();
        probe.Calls.Should().Be(0);
        bootstrapper.Tasks.Tasks.Should().ContainSingle().Which.State.Should().Be(SetupTaskState.Cancelled);
        bootstrapper.Tasks.ActiveTask.Should().BeNull();
    }

    [Fact]
    public async Task Cancellation_at_the_bootstrapper_return_boundary_does_not_complete_the_original_task()
    {
        using var cancellation = new CancellationTokenSource();
        var probe = new FinalProbe(DependencyReadiness.Ready);
        probe.Release.SetResult();
        using var bootstrapper = new DependencyBootstrapper([probe],
            new CallbackLogger(id => { if (id == 3) { cancellation.Cancel(); } }));
        var transitions = new List<SetupTaskState>();
        bootstrapper.Tasks.Changed += (_, _) =>
            transitions.Add(bootstrapper.Tasks.Tasks.Single().State);
        var workflow = new DependencySetupWorkflow(bootstrapper, new Installer(), new PowerShellSetup());

        var install = () => workflow.InstallLocalModelAsync(new InlineProgress(), cancellation.Token);

        await install.Should().ThrowAsync<OperationCanceledException>();
        probe.Exited.Should().BeTrue();
        transitions.Should().Equal(SetupTaskState.Running, SetupTaskState.Running, SetupTaskState.Cancelled);
        bootstrapper.Tasks.ActiveTask.Should().BeNull();
    }

    [Fact]
    public async Task Missing_required_final_verification_fails_the_original_task_without_success()
    {
        using var bootstrapper = new DependencyBootstrapper([], NullLogger<DependencyBootstrapper>.Instance);
        var transitions = new List<SetupTaskState>();
        bootstrapper.Tasks.Changed += (_, _) =>
            transitions.Add(bootstrapper.Tasks.Tasks.Single().State);
        var workflow = new DependencySetupWorkflow(bootstrapper, new Installer(), new PowerShellSetup());

        var install = () => workflow.InstallLocalModelAsync(new InlineProgress(), TestContext.Current.CancellationToken);

        await install.Should().ThrowAsync<InvalidOperationException>();
        transitions.Should().Equal(SetupTaskState.Running, SetupTaskState.Running, SetupTaskState.Failed);
        bootstrapper.Observations.Should().BeEmpty();
        bootstrapper.Tasks.ActiveTask.Should().BeNull();
    }

    [Fact]
    public async Task Cancellation_after_an_ignoring_installer_returns_does_not_enter_verification()
    {
        using var cancellation = new CancellationTokenSource();
        var probe = new FinalProbe(DependencyReadiness.Ready);
        using var bootstrapper = new DependencyBootstrapper([probe], NullLogger<DependencyBootstrapper>.Instance);
        var workflow = new DependencySetupWorkflow(bootstrapper, new Installer(),
            new PowerShellSetup());
        var progress = new InlineProgress(_ => cancellation.Cancel());

        var install = () => workflow.InstallLocalModelAsync(progress, cancellation.Token);

        await install.Should().ThrowAsync<OperationCanceledException>();
        probe.Calls.Should().Be(0);
        bootstrapper.Tasks.Tasks.Should().ContainSingle().Which.State.Should().Be(SetupTaskState.Cancelled);
        bootstrapper.Observations.Should().BeEmpty();
    }

    [Theory]
    [InlineData(DependencyReadiness.Ready)]
    [InlineData(DependencyReadiness.Missing)]
    [InlineData(DependencyReadiness.Failed)]
    public async Task Noncancelled_verification_preserves_success_and_readiness_failures(DependencyReadiness readiness)
    {
        var probe = new FinalProbe(readiness);
        probe.Release.SetResult();
        using var bootstrapper = new DependencyBootstrapper([probe], NullLogger<DependencyBootstrapper>.Instance);
        var workflow = new DependencySetupWorkflow(bootstrapper, new Installer(), new PowerShellSetup());
        var install = () => workflow.InstallLocalModelAsync(new InlineProgress(), TestContext.Current.CancellationToken);

        if (readiness == DependencyReadiness.Ready)
        {
            var result = await install();
            result.Statuses.Should().ContainSingle().Which.Should().Be(result.Inference);
            result.Inference.Readiness.Should().Be(DependencyReadiness.Ready);
            bootstrapper.Tasks.Tasks.Should().ContainSingle().Which.Should().BeEquivalentTo(
                new SetupTask(probe.TaskId, probe.TaskName, SetupTaskState.Completed, "Final verification.", 100));
        }
        else
        {
            await install.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Local inference did not pass the readiness check: Final verification.");
            bootstrapper.Tasks.Tasks.Should().ContainSingle().Which.State.Should().Be(SetupTaskState.Failed);
        }
        probe.Exited.Should().BeTrue();
        bootstrapper.Observations.Should().ContainSingle().Which.Status.Readiness.Should().Be(readiness);
        bootstrapper.Tasks.ActiveTask.Should().BeNull();
    }

    [Fact]
    public async Task Precancelled_installation_does_not_start_verification()
    {
        var probe = new FinalProbe(DependencyReadiness.Ready);
        using var bootstrapper = new DependencyBootstrapper([probe], NullLogger<DependencyBootstrapper>.Instance);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var workflow = new DependencySetupWorkflow(bootstrapper, new Installer(), new PowerShellSetup());

        var install = () => workflow.InstallLocalModelAsync(new InlineProgress(), cancellation.Token);

        await install.Should().ThrowAsync<OperationCanceledException>();
        probe.Calls.Should().Be(0);
        bootstrapper.Observations.Should().BeEmpty();
        bootstrapper.Tasks.Tasks.Should().ContainSingle().Which.State.Should().Be(SetupTaskState.Cancelled);
    }

    [Fact]
    public async Task Final_probe_exception_is_not_hidden_or_converted_to_success()
    {
        var probe = new FinalProbe(DependencyReadiness.Ready);
        probe.Release.SetException(new IOException("Verification failed."));
        using var bootstrapper = new DependencyBootstrapper([probe], NullLogger<DependencyBootstrapper>.Instance);
        var workflow = new DependencySetupWorkflow(bootstrapper, new Installer(), new PowerShellSetup());

        var install = () => workflow.InstallLocalModelAsync(new InlineProgress(), TestContext.Current.CancellationToken);

        await install.Should().ThrowAsync<IOException>().WithMessage("Verification failed.");
        bootstrapper.Observations.Should().BeEmpty();
        bootstrapper.Tasks.Tasks.Should().ContainSingle().Which.State.Should().Be(SetupTaskState.Failed);
        bootstrapper.Tasks.ActiveTask.Should().BeNull();
    }

    private sealed class FinalProbe(DependencyReadiness readiness) : ISetupDependencyProbe
    {
        public string TaskId => DependencySetupWorkflow.LocalModelTaskId;
        public string TaskName => "Local model inference (Ollama)";
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public int Calls { get; private set; }
        public bool Exited { get; private set; }

        public async ValueTask<DependencyStatus> ProbeAsync(CancellationToken cancellationToken)
        {
            Calls++;
            Token = cancellationToken;
            Entered.TrySetResult();
            await Release.Task;
            Exited = true;
            return new(TaskId, TaskName, readiness, "Final verification.");
        }
    }

    private sealed class Installer : ILocalModelSetup
    {
        public int Calls { get; private set; }
        public CancellationToken Token { get; private set; }

        public Task InstallAsync(IProgress<LocalModelSetupProgress> progress, CancellationToken cancellationToken)
        {
            Calls++;
            Token = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(new("Installed; verifying.", 90));
            return Task.CompletedTask;
        }
    }

    private sealed class PowerShellSetup : IPowerShellSetup
    {
        public string TaskId => "powershell.runtime";
        public string TaskName => "PowerShell";
        public Task InstallAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("Not requested.");
        public ValueTask<DependencyStatus> ProbeAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Not requested.");
    }

    private sealed class CallbackLogger(Action<int> callback) : ILogger<DependencyBootstrapper>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) => callback(eventId.Id);
    }

    private sealed class InlineProgress(Action<LocalModelSetupProgress>? report = null) : IProgress<LocalModelSetupProgress>
    {
        public void Report(LocalModelSetupProgress value) => report?.Invoke(value);
    }
}
